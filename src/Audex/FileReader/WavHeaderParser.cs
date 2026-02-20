using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Audex.Utils;

namespace Audex.FileReader
{
    /// <summary>
    /// Parses WAV RIFF headers to extract audio metadata.
    /// Handles non-standard chunk ordering and PCM/compressed formats.
    /// </summary>
    public static class WavHeaderParser
    {
        /// <summary>
        /// Parses a WAV file from an IStream.
        /// </summary>
        public static AudioFileInfo Parse(IStream stream, string fileName, long fileSize)
        {
            try
            {
                Logger.Debug("Parsing WAV file");

                // Reset stream position
                stream.Seek(0, 0, IntPtr.Zero);

                // Read RIFF header (12 bytes)
                byte[] riffHeader = StreamHelper.ReadBytes(stream, 12);

                // Check "RIFF" signature
                string riffSig = Encoding.ASCII.GetString(riffHeader, 0, 4);
                if (riffSig != "RIFF")
                {
                    return CreateError(fileName, fileSize, "Not a valid RIFF file");
                }

                // Check "WAVE" format
                string waveFormat = Encoding.ASCII.GetString(riffHeader, 8, 4);
                if (waveFormat != "WAVE")
                {
                    return CreateError(fileName, fileSize, "Not a valid WAVE file");
                }

                // Scan for "fmt " chunk
                ChunkInfo? fmtChunk = FindChunk(stream, "fmt ");
                if (fmtChunk == null)
                {
                    return CreateError(fileName, fileSize, "fmt chunk not found");
                }

                // Read fmt chunk data (minimum 16 bytes)
                byte[] fmtData = StreamHelper.ReadBytes(stream, Math.Min(fmtChunk.Value.Size, 1024));

                // Parse fmt chunk
                ushort audioFormat = BitConverter.ToUInt16(fmtData, 0);  // 1 = PCM
                ushort channels = BitConverter.ToUInt16(fmtData, 2);
                uint sampleRate = BitConverter.ToUInt32(fmtData, 4);
                uint byteRate = BitConverter.ToUInt32(fmtData, 8);
                ushort blockAlign = BitConverter.ToUInt16(fmtData, 12);
                ushort bitsPerSample = fmtData.Length >= 16 ? BitConverter.ToUInt16(fmtData, 14) : (ushort)0;

                // Scan for "data" chunk to get duration
                ChunkInfo? dataChunk = FindChunk(stream, "data");
                double duration = 0;

                if (dataChunk != null && audioFormat == 1 && byteRate > 0)
                {
                    // PCM format - calculate duration
                    duration = (double)dataChunk.Value.Size / byteRate;
                }
                else if (audioFormat != 1)
                {
                    Logger.Warn("Compressed WAV format detected - duration calculation may be inaccurate");
                }

                return new AudioFileInfo
                {
                    FileName = fileName,
                    FileSize = fileSize,
                    Format = audioFormat == 1 ? "WAV" : "WAV (compressed)",
                    SampleRate = (int)sampleRate,
                    BitDepth = bitsPerSample,
                    Channels = channels,
                    Duration = duration,
                    ParseSucceeded = true
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"WAV parsing failed: {ex.Message}", ex);
                return CreateError(fileName, fileSize, ex.Message);
            }
        }

        /// <summary>
        /// Finds a chunk by FourCC identifier in the RIFF stream.
        /// </summary>
        private static ChunkInfo? FindChunk(IStream stream, string fourCC)
        {
            try
            {
                // Start after RIFF header (12 bytes)
                stream.Seek(12, 0, IntPtr.Zero);

                byte[] chunkHeader = new byte[8];

                while (true)
                {
                    // Read chunk header (4 bytes ID + 4 bytes size)
                    int bytesRead = StreamHelper.TryReadBytes(stream, chunkHeader, 8);
                    if (bytesRead < 8)
                    {
                        // End of stream
                        return null;
                    }

                    string chunkId = Encoding.ASCII.GetString(chunkHeader, 0, 4);
                    uint chunkSize = BitConverter.ToUInt32(chunkHeader, 4);

                    if (chunkId == fourCC)
                    {
                        return new ChunkInfo { Id = chunkId, Size = (int)chunkSize };
                    }

                    // Skip to next chunk (align to 2-byte boundary)
                    int skipSize = (int)chunkSize;
                    if (skipSize % 2 != 0)
                        skipSize++;

                    stream.Seek(skipSize, 1, IntPtr.Zero); // STREAM_SEEK_CUR = 1
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"Chunk scan ended: {ex.Message}");
                return null;
            }
        }

        private static AudioFileInfo CreateError(string fileName, long fileSize, string error)
        {
            return new AudioFileInfo
            {
                FileName = fileName,
                FileSize = fileSize,
                Format = "WAV",
                ParseSucceeded = false,
                ParseError = error
            };
        }

        private struct ChunkInfo
        {
            public string Id;
            public int Size;
        }
    }
}
