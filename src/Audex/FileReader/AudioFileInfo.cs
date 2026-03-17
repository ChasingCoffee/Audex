namespace Audex.FileReader
{
    /// <summary>
    /// Data class holding parsed audio file metadata.
    /// Contains basic file information and parsed header data for supported formats.
    /// </summary>
    public class AudioFileInfo
    {
        /// <summary>
        /// File name (from IStream statistics).
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// File size in bytes.
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// Audio format (e.g., "WAV", "MP3", "FLAC", "OGG").
        /// </summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>
        /// Sample rate in Hz (e.g., 44100, 48000).
        /// 0 if not available or parsing failed.
        /// </summary>
        public int SampleRate { get; set; }

        /// <summary>
        /// Bit depth (e.g., 16, 24).
        /// 0 if not applicable (e.g., MP3) or not available.
        /// </summary>
        public int BitDepth { get; set; }

        /// <summary>
        /// Number of channels (1 = mono, 2 = stereo).
        /// 0 if not available or parsing failed.
        /// </summary>
        public int Channels { get; set; }

        /// <summary>
        /// Duration in seconds.
        /// 0 if not available or parsing failed.
        /// </summary>
        public double Duration { get; set; }

        /// <summary>
        /// Bit rate in kbps (for compressed formats like MP3).
        /// 0 if not applicable or not available.
        /// </summary>
        public int BitRate { get; set; }

        /// <summary>
        /// Indicates whether header parsing succeeded.
        /// </summary>
        public bool ParseSucceeded { get; set; } = true;

        /// <summary>
        /// Error message if parsing failed, null otherwise.
        /// </summary>
        public string? ParseError { get; set; }

        // --- Tag metadata (from TagLib#, nullable — hidden in UI when null) ---

        /// <summary>
        /// Track title from ID3/Vorbis tags. Null if tag is absent or empty.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Artist name joined from Performers array. Null if tag is absent or empty.
        /// </summary>
        public string? Artist { get; set; }

        /// <summary>
        /// Album name from tags. Null if tag is absent or empty.
        /// </summary>
        public string? Album { get; set; }

        // --- Music info (from TagReader.ReadMusicInfo, nullable — shown as dash when null) ---

        /// <summary>BPM (beats per minute) from file tags. Null if not present.</summary>
        public int? Bpm { get; set; }

        /// <summary>Musical key in standard notation (e.g., Am, C#m, F). Null if not present.</summary>
        public string? Key { get; set; }

        // --- Format state ---

        /// <summary>Whether this file is a module format (.mod, .xm, .it, .s3m).</summary>
        public bool IsModuleFormat { get; set; }

        /// <summary>Error message when format cannot be decoded. Null when playback succeeded.</summary>
        public string? FormatError { get; set; }
    }
}
