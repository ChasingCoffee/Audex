using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Audex.Utils;

namespace Audex.UI
{
    /// <summary>
    /// Disk cache for waveform peak arrays and frequency color arrays, keyed by SHA-256 content hash.
    /// Peak cache files are stored in %TEMP%\Audex\ with .wf extension.
    /// Color cache files use the same key with .wfc extension.
    /// Enforces a 50 MB size limit with oldest-first eviction across both file types.
    /// </summary>
    public static class WaveformCache
    {
        private const string CacheSubfolder = "Audex";
        private const string CacheExtension = ".wf";
        private const string ColorCacheExtension = ".wfc";
        private const byte ColorCacheVersion = 1;

        /// <summary>
        /// Computes a lowercase hex SHA-256 hash of the audio data to use as a cache key.
        /// </summary>
        public static string ComputeCacheKey(byte[] audioData)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(audioData);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Returns the full path for a cache file by key.
        /// Creates the cache directory if it does not exist.
        /// </summary>
        public static string GetCachePath(string key)
        {
            string dir = Path.Combine(Path.GetTempPath(), CacheSubfolder);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return Path.Combine(dir, key + CacheExtension);
        }

        /// <summary>
        /// Attempts to read a cached peak array. Returns null if not found or on any read error.
        /// On success, touches the file's LastWriteTime for LRU eviction ordering.
        /// </summary>
        public static float[]? ReadCache(string key)
        {
            try
            {
                string path = GetCachePath(key);
                if (!File.Exists(path))
                    return null;

                float[] peaks;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    int count = reader.ReadInt32();
                    if (count <= 0 || count > 1_000_000)
                        return null; // sanity check

                    peaks = new float[count];
                    for (int i = 0; i < count; i++)
                    {
                        peaks[i] = reader.ReadSingle();
                    }
                }

                // Touch the file to update LRU timestamp
                try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { }

                return peaks;
            }
            catch (Exception ex)
            {
                Logger.Error($"[WaveformCache] ReadCache failed for key {key}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Writes a peak array to the cache, then evicts if over the size limit.
        /// </summary>
        public static void WriteCache(string key, float[] peaks)
        {
            try
            {
                string path = GetCachePath(key);
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    writer.Write(peaks.Length);
                    foreach (float f in peaks)
                    {
                        writer.Write(f);
                    }
                }
                EvictIfNeeded();
            }
            catch (Exception ex)
            {
                Logger.Error($"[WaveformCache] WriteCache failed for key {key}: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to read a cached frequency color array from a versioned .wfc file.
        /// Returns null if not found, version mismatch, or on any read error.
        /// On success, touches the file's LastWriteTime for LRU eviction ordering.
        /// </summary>
        public static Color[]? ReadColorCache(string key)
        {
            string path = Path.Combine(Path.GetTempPath(), CacheSubfolder, key + ColorCacheExtension);
            try
            {
                if (!File.Exists(path))
                    return null;

                bool deleteInvalidVersion = false;
                Color[]? colors = null;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    // Read version byte — delete file if mismatched (forward compatibility)
                    byte version = reader.ReadByte();
                    if (version != ColorCacheVersion)
                    {
                        deleteInvalidVersion = true;
                    }
                    else
                    {
                        int count = reader.ReadInt32();
                        if (count <= 0 || count > 1_000_000)
                            return null; // sanity check

                        colors = new Color[count];
                        for (int i = 0; i < count; i++)
                        {
                            byte r = reader.ReadByte();
                            byte g = reader.ReadByte();
                            byte b = reader.ReadByte();
                            colors[i] = Color.FromArgb(r, g, b);
                        }
                    }
                }

                if (deleteInvalidVersion)
                {
                    try { File.Delete(path); } catch { }
                    return null;
                }

                // Touch the file to update LRU timestamp
                try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { }

                return colors;
            }
            catch (Exception ex)
            {
                Logger.Error($"[WaveformCache] ReadColorCache failed for key {key}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Writes a frequency color array to a versioned .wfc cache file.
        /// Stores version byte, count, then RGB bytes per color (no alpha — applied at render time).
        /// Calls EvictIfNeeded after write.
        /// </summary>
        public static void WriteColorCache(string key, Color[] colors)
        {
            string path = Path.Combine(Path.GetTempPath(), CacheSubfolder, key + ColorCacheExtension);
            try
            {
                // Ensure directory exists
                string dir = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    writer.Write(ColorCacheVersion);   // version byte
                    writer.Write(colors.Length);       // int32 count
                    foreach (Color c in colors)
                    {
                        writer.Write(c.R);             // 3 bytes per color (no alpha)
                        writer.Write(c.G);
                        writer.Write(c.B);
                    }
                }
                EvictIfNeeded();
            }
            catch (Exception ex)
            {
                Logger.Error($"[WaveformCache] WriteColorCache failed for key {key}: {ex.Message}");
            }
        }

        /// <summary>
        /// Evicts the oldest cache files until total size is below maxBytes (default 50 MB).
        /// Covers both .wf (peaks) and .wfc (colors) files — combined 50 MB limit.
        /// Sorts by LastWriteTime ascending (oldest first).
        /// </summary>
        public static void EvictIfNeeded(long maxBytes = 50L * 1024 * 1024)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), CacheSubfolder);
                if (!Directory.Exists(dir))
                    return;

                // Collect both .wf and .wfc files for combined eviction
                var dirInfo = new DirectoryInfo(dir);
                FileInfo[] wfFiles = dirInfo.GetFiles("*" + CacheExtension);
                FileInfo[] wfcFiles = dirInfo.GetFiles("*" + ColorCacheExtension);

                FileInfo[] files = wfFiles
                    .Concat(wfcFiles)
                    .OrderBy(f => f.LastWriteTimeUtc)
                    .ToArray();

                long totalSize = files.Sum(f => f.Length);

                foreach (FileInfo file in files)
                {
                    if (totalSize <= maxBytes)
                        break;

                    try
                    {
                        long fileSize = file.Length;
                        file.Delete();
                        totalSize -= fileSize;
                    }
                    catch
                    {
                        // Swallow individual delete failures
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[WaveformCache] EvictIfNeeded failed: {ex.Message}");
            }
        }
    }
}
