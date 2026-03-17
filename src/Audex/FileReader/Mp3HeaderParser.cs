using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Audex.Utils;

namespace Audex.FileReader
{
    /// <summary>
    /// Parses MP3 frame headers to extract audio metadata.
    /// Skips ID3v2 tags and estimates duration based on file size and bitrate.
    /// </summary>
    public static class Mp3HeaderParser
    {
        // MPEG1 Layer III bitrate table (in kbps)
        private static readonly int[] BitrateTable = {
            0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0
        };

        // MPEG1 sample rate table (in Hz)
        private static readonly int[] SampleRateTable = { 44100, 48000, 32000 };

        /// <summary>
        /// Parses an MP3 file from an IStream.
        /// </summary>
        public static AudioFileInfo Parse(IStream stream, string fileName, long fileSize)
        {
            try
            {
                Logger.Debug("Parsing MP3 file");

                // Reset stream position
                stream.Seek(0, 0, IntPtr.Zero);

                // Skip ID3v2 tag if present
                int dataOffset = SkipId3v2Tag(stream);

                // Find first valid MPEG frame
                FrameHeader? frame = FindFirstFrame(stream, dataOffset);

                if (frame == null)
                {
                    return CreateError(fileName, fileSize, "No valid MP3 frame found");
                }

                // Estimate duration based on file size and bitrate
                // This assumes CBR (constant bitrate) - may be inaccurate for VBR files
                long audioDataSize = fileSize - dataOffset;
                double duration = 0;

                if (frame.Value.BitRate > 0)
                {
                    duration = (double)(audioDataSize * 8) / (frame.Value.BitRate * 1000);
                }

                return new AudioFileInfo
                {
                    FileName = fileName,
                    FileSize = fileSize,
                    Format = "MP3",
                    SampleRate = frame.Value.SampleRate,
                    BitDepth = 0, // Not applicable for MP3
                    Channels = frame.Value.Channels,
                    Duration = duration,
                    BitRate = frame.Value.BitRate,
                    ParseSucceeded = true
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"MP3 parsing failed: {ex.Message}", ex);
                return CreateError(fileName, fileSize, ex.Message);
            }
        }

        /// <summary>
        /// Skips ID3v2 tag at the beginning of the stream.
        /// Returns the offset to the audio data.
        /// </summary>
        private static int SkipId3v2Tag(IStream stream)
        {
            try
            {
                byte[] header = StreamHelper.ReadBytes(stream, 10);

                // Check for "ID3" signature
                if (header[0] == 'I' && header[1] == 'D' && header[2] == '3')
                {
                    // Parse syncsafe integer (4 bytes, 7 bits per byte)
                    int tagSize = (header[6] << 21) | (header[7] << 14) | (header[8] << 7) | header[9];
                    int totalSize = 10 + tagSize;

                    Logger.Debug($"ID3v2 tag found, size: {totalSize} bytes");

                    // Seek past the tag
                    stream.Seek(totalSize, 0, IntPtr.Zero);
                    return totalSize;
                }
                else
                {
                    // No ID3v2 tag, reset to beginning
                    stream.Seek(0, 0, IntPtr.Zero);
                    return 0;
                }
            }
            catch
            {
                // If reading fails, assume no tag
                stream.Seek(0, 0, IntPtr.Zero);
                return 0;
            }
        }

        /// <summary>
        /// Finds the first valid MPEG frame header.
        /// Scans up to 64KB to find frame sync.
        /// </summary>
        private static FrameHeader? FindFirstFrame(IStream stream, int startOffset)
        {
            const int MaxScanSize = 64 * 1024; // 64KB
            byte[] buffer = new byte[4];
            int bytesScanned = 0;

            while (bytesScanned < MaxScanSize)
            {
                // Read one byte at a time looking for frame sync (0xFF)
                int bytesRead = StreamHelper.TryReadBytes(stream, buffer, 1);
                if (bytesRead < 1)
                {
                    return null; // End of stream
                }

                if (buffer[0] == 0xFF)
                {
                    // Possible frame sync, read next 3 bytes
                    bytesRead = StreamHelper.TryReadBytes(stream, buffer, 3, offset: 1);
                    if (bytesRead < 3)
                    {
                        return null;
                    }

                    // Check if this is a valid frame header
                    FrameHeader? frame = ParseFrameHeader(buffer);
                    if (frame != null)
                    {
                        return frame;
                    }

                    // Not a valid frame, continue scanning
                    // Rewind 3 bytes and continue
                    stream.Seek(-3, 1, IntPtr.Zero);
                }

                bytesScanned++;
            }

            return null;
        }

        /// <summary>
        /// Parses a 4-byte MPEG frame header.
        /// </summary>
        private static FrameHeader? ParseFrameHeader(byte[] header)
        {
            try
            {
                // Check frame sync (11 bits set)
                if ((header[0] != 0xFF) || ((header[1] & 0xE0) != 0xE0))
                {
                    return null;
                }

                // Parse header fields
                int version = (header[1] >> 3) & 0x03;      // MPEG version
                int layer = (header[1] >> 1) & 0x03;        // Layer
                int bitrateIndex = (header[2] >> 4) & 0x0F; // Bitrate index
                int sampleRateIndex = (header[2] >> 2) & 0x03; // Sample rate index
                int channelMode = (header[3] >> 6) & 0x03;  // Channel mode

                // We only support MPEG1 Layer III for now
                if (version != 3 || layer != 1) // version 3 = MPEG1, layer 1 = Layer III
                {
                    return null;
                }

                // Validate indices
                if (bitrateIndex == 0 || bitrateIndex == 15 || sampleRateIndex == 3)
                {
                    return null;
                }

                // Look up values
                int bitrate = BitrateTable[bitrateIndex];
                int sampleRate = SampleRateTable[sampleRateIndex];
                int channels = (channelMode == 3) ? 1 : 2; // mode 3 = mono, others = stereo

                return new FrameHeader
                {
                    BitRate = bitrate,
                    SampleRate = sampleRate,
                    Channels = channels
                };
            }
            catch
            {
                return null;
            }
        }

        private static AudioFileInfo CreateError(string fileName, long fileSize, string error)
        {
            return new AudioFileInfo
            {
                FileName = fileName,
                FileSize = fileSize,
                Format = "MP3",
                ParseSucceeded = false,
                ParseError = error
            };
        }

        private struct FrameHeader
        {
            public int BitRate;
            public int SampleRate;
            public int Channels;
        }
    }
}
