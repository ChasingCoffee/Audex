using System;
using System.IO;
using System.Linq;
using System.Text;
using Audex.UI;
using Audex.Utils;

namespace Audex.Audio
{
    /// <summary>
    /// Binary disk cache for BPM/key analysis results.
    /// Cache files stored in %TEMP%\Audex\analysis\ with .bka extension.
    /// Uses content-hash cache keys (SHA-256) reusing WaveformCache.ComputeCacheKey.
    /// LRU eviction: keeps newest 2000 entries.
    /// </summary>
    public static class AnalysisCache
    {
        private const string CacheSubfolder = "Audex";
        private const string AnalysisSubfolder = "analysis";
        private const string CacheExtension = ".bka";
        private const byte CacheVersion = 1;
        private const int MaxEntries = 2000;

        // Bit flags for failFlags byte in cache binary
        private const byte BpmFailedFlag = 0x01;
        private const byte KeyFailedFlag = 0x02;

        /// <summary>
        /// Returns the full path for an analysis cache file by key.
        /// Creates the cache directory if it does not exist.
        /// </summary>
        private static string GetCachePath(string key)
        {
            string dir = Path.Combine(Path.GetTempPath(), CacheSubfolder, AnalysisSubfolder);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return Path.Combine(dir, key + CacheExtension);
        }

        /// <summary>
        /// Reads a cached AnalysisResult by cache key. Returns null if not found, version mismatch, or error.
        /// Touches LastWriteTimeUtc on hit for LRU ordering.
        /// </summary>
        public static AnalysisResult? Read(string cacheKey)
        {
            try
            {
                string path = GetCachePath(cacheKey);
                if (!File.Exists(path))
                    return null;

                bool deleteInvalidVersion = false;
                AnalysisResult? result = null;

                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (BinaryReader reader = new BinaryReader(fs, Encoding.UTF8))
                {
                    // Version check
                    byte version = reader.ReadByte();
                    if (version != CacheVersion)
                    {
                        deleteInvalidVersion = true;
                    }
                    else
                    {
                        float bpm = reader.ReadSingle();
                        byte keyLen = reader.ReadByte();
                        byte[] keyUtf8 = keyLen > 0 ? reader.ReadBytes(keyLen) : Array.Empty<byte>();
                        float bpmConf = reader.ReadSingle();
                        float keyConf = reader.ReadSingle();
                        long ticks = reader.ReadInt64();  // stored but not currently used (LRU via file mtime)
                        byte failFlags = reader.ReadByte();

                        bool bpmFailed = (failFlags & BpmFailedFlag) != 0;
                        bool keyFailed = (failFlags & KeyFailedFlag) != 0;

                        result = new AnalysisResult
                        {
                            DetectedBpm = (!bpmFailed && bpm > 0f) ? (int?)Math.Round(bpm) : null,
                            DetectedKey = (!keyFailed && keyLen > 0) ? Encoding.UTF8.GetString(keyUtf8) : null,
                            BpmConfidence = bpmConf,
                            KeyConfidence = keyConf,
                            BpmFailed = bpmFailed,
                            KeyFailed = keyFailed,
                            FailureReason = (bpmFailed && keyFailed) ? "unable to detect" : null
                        };
                    }
                }

                if (deleteInvalidVersion)
                {
                    try { File.Delete(path); } catch { }
                    return null;
                }

                // Touch for LRU ordering
                try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { }

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"[AnalysisCache] Read failed for key {cacheKey}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Writes an AnalysisResult to the cache. Evicts oldest entries if over MaxEntries.
        /// </summary>
        public static void Write(string cacheKey, AnalysisResult result)
        {
            try
            {
                string path = GetCachePath(cacheKey);

                byte failFlags = 0;
                if (result.BpmFailed) failFlags |= BpmFailedFlag;
                if (result.KeyFailed) failFlags |= KeyFailedFlag;

                float bpmValue = result.DetectedBpm.HasValue ? (float)result.DetectedBpm.Value : 0f;
                byte[] keyUtf8 = result.DetectedKey != null
                    ? Encoding.UTF8.GetBytes(result.DetectedKey)
                    : Array.Empty<byte>();

                // Truncate key to 255 bytes max (fits in a single byte length field)
                if (keyUtf8.Length > 255)
                    keyUtf8 = Encoding.UTF8.GetBytes(result.DetectedKey!.Substring(0, 10));

                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (BinaryWriter writer = new BinaryWriter(fs, Encoding.UTF8))
                {
                    writer.Write(CacheVersion);                     // byte: version
                    writer.Write(bpmValue);                         // float: bpm
                    writer.Write((byte)keyUtf8.Length);             // byte: key string length
                    if (keyUtf8.Length > 0)
                        writer.Write(keyUtf8);                      // bytes: key UTF-8
                    writer.Write(result.BpmConfidence);             // float: bpm confidence
                    writer.Write(result.KeyConfidence);             // float: key confidence
                    writer.Write(DateTime.UtcNow.Ticks);            // long: timestamp ticks
                    writer.Write(failFlags);                        // byte: fail flags
                }

                EvictIfNeeded();
            }
            catch (Exception ex)
            {
                Logger.Error($"[AnalysisCache] Write failed for key {cacheKey}: {ex.Message}");
            }
        }

        /// <summary>
        /// Deletes ALL analysis cache files (.bka) from the analysis cache directory.
        /// Called from the settings overlay "Clear analysis cache" button.
        /// </summary>
        public static void ClearAll()
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), CacheSubfolder, AnalysisSubfolder);
                if (!Directory.Exists(dir))
                    return;

                string[] files = Directory.GetFiles(dir, "*" + CacheExtension);
                int deleted = 0;
                foreach (string file in files)
                {
                    try { File.Delete(file); deleted++; } catch { }
                }
                Logger.Info($"[AnalysisCache] ClearAll: deleted {deleted} cache files");
            }
            catch (Exception ex)
            {
                Logger.Error($"[AnalysisCache] ClearAll failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Deletes the cache entry for the given key. Used for re-analyze flows.
        /// </summary>
        public static void Delete(string cacheKey)
        {
            try
            {
                string path = GetCachePath(cacheKey);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Logger.Error($"[AnalysisCache] Delete failed for key {cacheKey}: {ex.Message}");
            }
        }

        /// <summary>
        /// Evicts oldest cache entries (by LastWriteTimeUtc) if entry count exceeds MaxEntries.
        /// Swallows individual delete failures.
        /// </summary>
        private static void EvictIfNeeded()
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), CacheSubfolder, AnalysisSubfolder);
                if (!Directory.Exists(dir))
                    return;

                var dirInfo = new DirectoryInfo(dir);
                FileInfo[] files = dirInfo.GetFiles("*" + CacheExtension);

                if (files.Length <= MaxEntries)
                    return;

                // Sort oldest first and delete until within limit
                var sorted = files.OrderBy(f => f.LastWriteTimeUtc).ToArray();
                int toDelete = sorted.Length - MaxEntries;

                for (int i = 0; i < toDelete; i++)
                {
                    try { sorted[i].Delete(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[AnalysisCache] EvictIfNeeded failed: {ex.Message}");
            }
        }
    }
}
