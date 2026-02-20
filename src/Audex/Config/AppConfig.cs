using System.Collections.Generic;

namespace Audex.Config
{
    /// <summary>
    /// POCO class holding application configuration values.
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// List of supported audio file extensions (e.g., ".wav", ".mp3").
        /// Core formats are always available; plugin-dependent formats (.aac, .m4a, .wma, .opus)
        /// require the corresponding BASS plugin DLL to be present at registration time.
        /// Module formats (.mod, .xm, .it, .s3m) use BASS.MusicLoad (built into core BASS).
        /// Default: all formats including module formats.
        /// </summary>
        public List<string> SupportedExtensions { get; set; } = new List<string>
        {
            ".wav", ".mp3", ".flac", ".aiff", ".aif", ".ogg",
            ".aac", ".m4a", ".wma", ".opus",
            ".mod", ".xm", ".it", ".s3m"
        };

        /// <summary>
        /// Log level for the application.
        /// Default: "info"
        /// </summary>
        public string LogLevel { get; set; } = "info";

        /// <summary>
        /// Debounce delay in milliseconds for file operations.
        /// Default: 150ms
        /// </summary>
        public int DebounceMs { get; set; } = 150;

        // --- Audio section ---

        /// <summary>
        /// Playback volume level (0.0 to 1.0). Default: 0.5 (50%).
        /// </summary>
        public float Volume { get; set; } = 0.5f;

        /// <summary>
        /// Whether audio output is muted. Default: false.
        /// </summary>
        public bool IsMuted { get; set; } = false;

        // --- Waveform section ---

        /// <summary>
        /// Whether frequency color mode is active for waveform display. Default: true (colored on first use).
        /// </summary>
        public bool WaveformColorMode { get; set; } = true;

        // --- Analysis section ---

        /// <summary>
        /// Whether BPM/key detection is enabled for files without tags. Default: true.
        /// </summary>
        public bool EnableBpmKeyDetection { get; set; } = true;

        /// <summary>
        /// Key profile selection strategy for key detection.
        /// Supported values: "auto", "krumhansl", "temperley".
        /// Default: "auto".
        /// </summary>
        public string KeyDetectionProfile { get; set; } = "auto";

        // --- Playback section ---

        /// <summary>
        /// Whether autoplay is enabled. When true, selecting a file auto-plays after AutoplayDelayMs.
        /// Default: false (off by default for first-time users).
        /// </summary>
        public bool Autoplay { get; set; } = false;

        /// <summary>
        /// Delay in milliseconds before autoplay triggers after file selection.
        /// Default: 500ms.
        /// </summary>
        public int AutoplayDelayMs { get; set; } = 500;

        /// <summary>
        /// Whether loop playback is enabled. When true, track restarts when it ends.
        /// Default: false.
        /// </summary>
        public bool Loop { get; set; } = false;

        // --- Device section ---

        /// <summary>
        /// WASAPI device index to use for playback. -1 = system default device.
        /// Default: -1.
        /// </summary>
        public int WasapiDeviceIndex { get; set; } = -1;

        // --- Display section ---

        /// <summary>
        /// Waveform height preset. Valid values: "Small", "Medium", "Large".
        /// Default: "Medium".
        /// </summary>
        public string WaveformHeightPreset { get; set; } = "Medium";
    }
}
