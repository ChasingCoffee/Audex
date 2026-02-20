using System;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
using Audex.Utils;

namespace Audex.FileReader
{
    /// <summary>
    /// Factory that selects the appropriate audio header parser based on file extension.
    /// Returns partial AudioFileInfo for unsupported formats.
    /// </summary>
    public static class AudioHeaderParserFactory
    {
        /// <summary>
        /// Parses audio file metadata from an IStream.
        /// Routes to format-specific parsers based on file extension.
        /// </summary>
        /// <param name="stream">IStream containing the file data</param>
        /// <param name="fileName">File name (for extension detection)</param>
        /// <param name="fileSize">File size in bytes</param>
        /// <returns>AudioFileInfo with parsed metadata or error information</returns>
        public static AudioFileInfo Parse(IStream stream, string fileName, long fileSize)
        {
            string ext = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

            try
            {
                // If extension is empty (filename unknown from IStream), detect format from magic bytes
                if (string.IsNullOrEmpty(ext))
                {
                    ext = DetectFormatFromStream(stream);
                    Logger.Debug($"Detected format from magic bytes: {ext}");
                }

                Logger.Debug($"Parsing audio file: {fileName} (ext: {ext})");

                return ext switch
                {
                    ".wav" => WavHeaderParser.Parse(stream, fileName, fileSize),
                    ".mp3" => Mp3HeaderParser.Parse(stream, fileName, fileSize),
                    ".flac" => FlacHeaderParser.Parse(stream, fileName, fileSize),

                    // Unsupported formats - return partial info (BASS provides actual metadata)
                    ".aiff" or ".aif" => CreateUnsupportedFormat(fileName, fileSize, "AIFF"),
                    ".ogg" => CreateUnsupportedFormat(fileName, fileSize, "OGG"),
                    ".aac" => CreateUnsupportedFormat(fileName, fileSize, "AAC"),
                    ".wma" => CreateUnsupportedFormat(fileName, fileSize, "WMA"),
                    ".opus" => CreateUnsupportedFormat(fileName, fileSize, "OPUS"),
                    ".m4a" => CreateUnsupportedFormat(fileName, fileSize, "M4A"),

                    // Module formats — BASS MusicLoad provides all metadata, no header parser needed
                    ".mod" => CreateUnsupportedFormat(fileName, fileSize, "MOD"),
                    ".xm"  => CreateUnsupportedFormat(fileName, fileSize, "XM"),
                    ".it"  => CreateUnsupportedFormat(fileName, fileSize, "IT"),
                    ".s3m" => CreateUnsupportedFormat(fileName, fileSize, "S3M"),

                    // Unknown extension
                    _ => CreateUnsupportedFormat(fileName, fileSize,
                        string.IsNullOrEmpty(ext) ? "Audio" : ext.TrimStart('.').ToUpperInvariant())
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"Header parsing failed for {fileName}: {ex.Message}", ex);
                return new AudioFileInfo
                {
                    FileName = fileName,
                    FileSize = fileSize,
                    Format = ext.TrimStart('.').ToUpperInvariant(),
                    ParseSucceeded = false,
                    ParseError = ex.Message
                };
            }
        }

        /// <summary>
        /// Detects audio format by reading magic bytes from the stream.
        /// Resets stream position to 0 after reading.
        /// </summary>
        private static string DetectFormatFromStream(IStream stream)
        {
            try
            {
                stream.Seek(0, 0, IntPtr.Zero);
                byte[] header = StreamHelper.ReadBytes(stream, 16);
                stream.Seek(0, 0, IntPtr.Zero);

                // RIFF....WAVE = WAV
                if (header.Length >= 12 &&
                    header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F' &&
                    header[8] == 'W' && header[9] == 'A' && header[10] == 'V' && header[11] == 'E')
                    return ".wav";

                // fLaC = FLAC
                if (header.Length >= 4 &&
                    header[0] == 'f' && header[1] == 'L' && header[2] == 'a' && header[3] == 'C')
                    return ".flac";

                // ID3 = MP3 with ID3 tag
                if (header.Length >= 3 && header[0] == 'I' && header[1] == 'D' && header[2] == '3')
                    return ".mp3";

                // FF FB, FF F3, FF F2 = MP3 frame sync
                if (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
                    return ".mp3";

                // OggS = OGG
                if (header.Length >= 4 &&
                    header[0] == 'O' && header[1] == 'g' && header[2] == 'g' && header[3] == 'S')
                    return ".ogg";

                // FORM....AIFF = AIFF
                if (header.Length >= 12 &&
                    header[0] == 'F' && header[1] == 'O' && header[2] == 'R' && header[3] == 'M' &&
                    header[8] == 'A' && header[9] == 'I' && header[10] == 'F' && header[11] == 'F')
                    return ".aiff";

                // XM: starts with "Extended Module:" (16 bytes)
                if (header.Length >= 16 &&
                    header[0]  == 'E' && header[1]  == 'x' && header[2]  == 't' && header[3]  == 'e' &&
                    header[4]  == 'n' && header[5]  == 'd' && header[6]  == 'e' && header[7]  == 'd' &&
                    header[8]  == ' ' && header[9]  == 'M' && header[10] == 'o' && header[11] == 'd' &&
                    header[12] == 'u' && header[13] == 'l' && header[14] == 'e' && header[15] == ':')
                    return ".xm";

                // IT: starts with "IMPM"
                if (header.Length >= 4 &&
                    header[0] == 'I' && header[1] == 'M' && header[2] == 'P' && header[3] == 'M')
                    return ".it";

                // S3M: byte at offset 0x1C (28) = 0x1A and bytes at offset 0x2C (44) = "SCRM"
                // Requires reading at least 48 bytes — attempt a separate read if needed
                // (keep it simple: only detect if we can read enough)
                // MOD: common signatures at offset 1080 require reading 1084+ bytes
                // Skip MOD/S3M magic detection here since extension-based routing is primary path
                // and reading 1084 bytes from every unknown file has high cost

                return string.Empty;
            }
            catch
            {
                try { stream.Seek(0, 0, IntPtr.Zero); } catch { }
                return string.Empty;
            }
        }

        /// <summary>
        /// Creates an AudioFileInfo for formats without dedicated parsers.
        /// These formats will show filename and filesize, but no audio metadata.
        /// </summary>
        private static AudioFileInfo CreateUnsupportedFormat(string fileName, long fileSize, string format)
        {
            Logger.Debug($"Format {format} has no dedicated parser - returning basic info only");

            return new AudioFileInfo
            {
                FileName = fileName,
                FileSize = fileSize,
                Format = format,
                SampleRate = 0,
                BitDepth = 0,
                Channels = 0,
                Duration = 0,
                BitRate = 0,
                ParseSucceeded = true, // Not an error - just no metadata available
                ParseError = null
            };
        }
    }
}
