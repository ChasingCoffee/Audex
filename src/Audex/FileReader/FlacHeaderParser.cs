using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Audex.Utils;

namespace Audex.FileReader
{
    /// <summary>
    /// Parses FLAC STREAMINFO metadata blocks to extract audio metadata.
    /// Handles bit-packed STREAMINFO structure with careful bit shifting.
    /// </summary>
    public static class FlacHeaderParser
    {
        /// <summary>
        /// Parses a FLAC file from an IStream.
        /// </summary>
        public static AudioFileInfo Parse(IStream stream, string fileName, long fileSize)
        {
            try
            {
                Logger.Debug("Parsing FLAC file");

                // Reset stream position
                stream.Seek(0, 0, IntPtr.Zero);

                // Read FLAC stream marker (4 bytes: "fLaC")
                byte[] marker = StreamHelper.ReadBytes(stream, 4);
                string flacMarker = Encoding.ASCII.GetString(marker);

                if (flacMarker != "fLaC")
                {
                    return CreateError(fileName, fileSize, "Not a valid FLAC file");
                }

                // Read METADATA_BLOCK_HEADER (4 bytes)
                byte[] blockHeader = StreamHelper.ReadBytes(stream, 4);

                byte blockType = (byte)(blockHeader[0] & 0x7F); // Lower 7 bits
                bool isLast = (blockHeader[0] & 0x80) != 0;      // High bit

                // Block length (3 bytes, big-endian)
                int blockLength = (blockHeader[1] << 16) | (blockHeader[2] << 8) | blockHeader[3];

                // Block type 0 = STREAMINFO
                if (blockType != 0)
                {
                    return CreateError(fileName, fileSize, "STREAMINFO block not found");
                }

                // STREAMINFO is always 34 bytes
                if (blockLength < 34)
                {
                    return CreateError(fileName, fileSize, "Invalid STREAMINFO block size");
                }

                // Read STREAMINFO block (34 bytes)
                byte[] streamInfo = StreamHelper.ReadBytes(stream, 34);

                // Parse STREAMINFO fields (bit-packed structure)
                // Bytes 0-1: Minimum block size (16 bits)
                // Bytes 2-3: Maximum block size (16 bits)
                // Bytes 4-6: Minimum frame size (24 bits)
                // Bytes 7-9: Maximum frame size (24 bits)
                // Bytes 10-17: Sample rate (20 bits), channels (3 bits), bit depth (5 bits), total samples (36 bits)

                // Sample rate: bytes 10-12, upper 20 bits
                int sampleRate = (streamInfo[10] << 12) | (streamInfo[11] << 4) | ((streamInfo[12] >> 4) & 0x0F);

                // Channels: next 3 bits (bits 4-6 of byte 12)
                int channels = ((streamInfo[12] >> 1) & 0x07) + 1; // Stored as channels-1

                // Bit depth: next 5 bits (bit 0 of byte 12 + bits 7-3 of byte 13)
                int bitDepth = (((streamInfo[12] & 0x01) << 4) | ((streamInfo[13] >> 3) & 0x1F)) + 1; // Stored as bps-1

                // Total samples: next 36 bits (bits 2-0 of byte 13 + bytes 14-17)
                long totalSamples = ((long)(streamInfo[13] & 0x07) << 32) |
                                   ((long)streamInfo[14] << 24) |
                                   ((long)streamInfo[15] << 16) |
                                   ((long)streamInfo[16] << 8) |
                                   streamInfo[17];

                // Calculate duration
                double duration = 0;
                if (sampleRate > 0)
                {
                    duration = (double)totalSamples / sampleRate;
                }

                return new AudioFileInfo
                {
                    FileName = fileName,
                    FileSize = fileSize,
                    Format = "FLAC",
                    SampleRate = sampleRate,
                    BitDepth = bitDepth,
                    Channels = channels,
                    Duration = duration,
                    ParseSucceeded = true
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"FLAC parsing failed: {ex.Message}", ex);
                return CreateError(fileName, fileSize, ex.Message);
            }
        }

        private static AudioFileInfo CreateError(string fileName, long fileSize, string error)
        {
            return new AudioFileInfo
            {
                FileName = fileName,
                FileSize = fileSize,
                Format = "FLAC",
                ParseSucceeded = false,
                ParseError = error
            };
        }
    }
}
