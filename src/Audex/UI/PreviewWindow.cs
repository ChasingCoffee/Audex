using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Windows.Forms;
using Audex.Audio;
using Audex.Config;
using Audex.FileReader;
using Audex.Interop;
using Audex.Utils;

namespace Audex.UI
{
    /// <summary>
    /// WinForms UserControl for the preview pane.
    /// Uses double-buffered GDI+ rendering via OnPaint.
    /// Created on the STA thread in the handler constructor, then reparented
    /// under Explorer's preview pane HWND via SetParent.
    ///
    /// Integrates AudioPlayer for playback: position timer polls every 250ms on the STA thread,
    /// mouse events drive play/pause/stop/seek/volume changes.
    ///
    /// Integrates WaveformGenerator, WaveformCache, and WaveformRenderer for waveform visualization
    /// with progressive reveal, click-to-seek, drag-to-scrub, and hover guide/tooltip.
    ///
    /// Integrates BpmKeyAnalyzer, AnalysisCache for background BPM/key detection with caching,
    /// progress display, re-analyze button, and cancellation on file switch.
    /// </summary>
    public class PreviewWindow : UserControl
    {
        // --- Content state ---
        private AudioFileInfo? _currentFileInfo;
        private bool _showError;
        private string? _errorMessage;
        private string? _formatError; // Non-null when format cannot be decoded (shows in waveform area)

        // --- Loading state ---
        private bool _isLoading;
        private int _spinnerFrame;
        private Timer? _spinnerTimer;
        private Timer? _loadingDelayTimer;

        private const int LOADING_DELAY_MS = 200;
        private const int SPINNER_INTERVAL_MS = 100;

        // --- Audio player state ---
        private AudioPlayer? _player;
        private Timer? _positionTimer;
        // ~30fps: fast enough that the playhead reads as a continuous sweep rather than visible
        // steps. The redraw this triggers (WaveformRenderer.Draw + control bar) is cheap — a few
        // hundred clipped GDI+ fills — so there's plenty of headroom below any real CPU concern.
        private const int POSITION_TIMER_INTERVAL_MS = 33;

        // --- Control interaction state ---
        private HitZone _hoveredZone = HitZone.None;
        private HitZone _pressedZone = HitZone.None;
        private bool _isSeeking;

        // --- Volume state (loaded from config, persisted on change) ---
        private float _volume;
        private bool _isMuted;

        // --- Autoplay/loop state (loaded from config, persisted on change) ---
        private bool _isAutoplay;
        private bool _isLoop;

        // --- Waveform state ---
        private float[]? _waveformPeaks;          // Canonical peak array (~2000 entries)
        private int _waveformBarsReady;           // Count of bars ready for progressive reveal
        private bool _waveformUnavailable;        // True if generation failed
        private int _currentGenerationId;         // Incremented on each new file to prevent stale callbacks
        private System.Threading.CancellationTokenSource? _waveCts; // Cancels in-progress generation on file switch

        // --- Waveform interaction state ---
        private bool _isWaveformDragging;         // True during click-and-drag on waveform
        private double _waveformDragPosition;     // Seconds -- visual-only during drag
        private Point _waveformHoverPoint;        // Current mouse position over waveform
        private bool _isHoveringWaveform;         // True when cursor is over waveform area

        // --- Frequency color state ---
        private Color[]? _waveformColors;         // null until frequency analysis completes
        private bool _isWaveformColorMode;        // loaded from config, toggled by button
        private bool _isToggleHovered;            // hover state for toggle button
        private bool _isTogglePressed;            // press state for toggle button

        // --- Cached waveform bounds (updated in OnPaint for targeted Invalidate) ---
        private Rectangle _waveformBounds;
        private Rectangle _waveformToggleBounds;
        private ControlBarRenderer.ControlBarLayout _controlBarLayout = ControlBarRenderer.ControlBarLayout.Empty;

        // --- Analysis state ---
        private AnalysisResult? _analysisResult;       // Current analysis result (null = not analyzed)
        private bool _isAnalyzing;                      // True while background analysis is running
        private float _analysisProgress;                // 0.0-1.0 progress
        private bool _isReanalyzing;                    // True during re-analysis (dims old values)
        private int _currentAnalysisId;                 // Incremented on each new analysis to prevent stale callbacks
        private System.Threading.CancellationTokenSource? _analysisCts;
        private DateTime _lastReanalyzeTime = DateTime.MinValue; // Cooldown tracking
        private const int ANALYSIS_DELAY_MS = 800;      // Delay before starting analysis
        private const int REANALYZE_COOLDOWN_MS = 2000;  // 2-second cooldown
        private bool _isReanalyzeHovered;                // Hover state for re-analyze button

        // Analysis trigger state — set from AudioPreviewHandler, read by StartBpmKeyAnalysis
        private bool _hasBpmTag;   // True if file has BPM from tags (skip BPM analysis)
        private bool _hasKeyTag;   // True if file has Key from tags (skip key analysis)

        // Audio data reference for re-analyze support
        private byte[]? _currentAudioData;
        private string? _currentCacheKey;
        private bool _isModuleFormat;
        private double _currentDuration;

        // --- Cached metadata bounds (updated in OnPaint for targeted Invalidate) ---
        private Rectangle _metadataBounds;
        private Rectangle _reanalyzeButtonBounds;

        // --- Owner-drawn tooltip state (replaces WinForms ToolTip which cannot display in prevhost.exe) ---
        private string? _tooltipText;
        private Point _tooltipPosition;
        private System.Windows.Forms.Timer? _tooltipTimer;
        private bool _tooltipVisible;

        // --- Settings overlay state ---
        private bool _settingsOpen;                                  // True when overlay is visible
        private Rectangle _settingsOverlayBounds;                    // Cached overlay bounds
        private SettingsOverlayLayout _settingsOverlayLayout = SettingsOverlayLayout.Empty;
        private Rectangle _gearIconRect;                             // Cached gear icon bounds
        private List<(int Index, string Name)>? _wasapiDevices;     // Enumerated once on open
        private bool _isDeviceDropdownOpen;                          // Dropdown expanded state
        private int _selectedDeviceDropdownIndex = -1;              // Highlighted item in dropdown
        private bool _isKeyProfileDropdownOpen;                      // Analysis profile dropdown expanded state
        private bool _isGearHovered;                                 // Hover state for gear icon

        // --- Waveform height preset ---
        private string _waveformHeightPreset = "Medium";            // Loaded from config

        // --- Cached DPI scale (updated in OnPaint and OnSizeChanged to avoid CreateGraphics() GDI handle leaks) ---
        private float _dpiScale = 1.0f;

        // --- Cached config and throttled persistence ---
        private AppConfig _config = new AppConfig();
        private Timer? _volumeSaveTimer;
        private bool _hasPendingVolumeSave;
        private const int VOLUME_SAVE_DEBOUNCE_MS = 200;

        public PreviewWindow()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);
            BackColor = Color.White;

            // Load persisted volume/mute, color mode, autoplay/loop, and height preset from config
            _config = ConfigManager.Load();
            _volume = _config.Volume;
            _isMuted = _config.IsMuted;
            _isWaveformColorMode = _config.WaveformColorMode;
            _isAutoplay = _config.Autoplay;
            _isLoop = _config.Loop;
            _waveformHeightPreset = _config.WaveformHeightPreset;

            // Initialize position timer (stopped; started when playback begins)
            _positionTimer = new Timer { Interval = POSITION_TIMER_INTERVAL_MS };
            _positionTimer.Tick += OnPositionTimerTick;

            // Debounce high-frequency volume writes during slider drag.
            _volumeSaveTimer = new Timer { Interval = VOLUME_SAVE_DEBOUNCE_MS };
            _volumeSaveTimer.Tick += (s, e) =>
            {
                _volumeSaveTimer?.Stop();
                if (!_hasPendingVolumeSave) return;
                _hasPendingVolumeSave = false;
                SaveVolumeConfig();
            };

            // Owner-drawn tooltip delay timer (400ms matches standard tooltip behavior)
            _tooltipTimer = new System.Windows.Forms.Timer { Interval = 400 };
            _tooltipTimer.Tick += (s, e) =>
            {
                _tooltipTimer.Stop();
                _tooltipVisible = true;
                Invalidate(); // trigger repaint to show tooltip
            };
        }

        /// <summary>
        /// Stores the AudioPlayer reference and subscribes to its events.
        /// Call once from AudioPreviewHandler constructor (STA thread).
        /// </summary>
        public void SetPlayer(AudioPlayer player)
        {
            if (_player != null)
            {
                _player.StateChanged -= OnPlayerStateChanged;
                _player.PlaybackEnded -= OnPlaybackEnded;
            }

            _player = player;
            _player.StateChanged += OnPlayerStateChanged;
            _player.PlaybackEnded += OnPlaybackEnded;
        }

        // -------------------------------------------------------------------------
        // Content update methods
        // -------------------------------------------------------------------------

        /// <summary>
        /// Updates the preview content with new file info.
        /// When info.FormatError is non-null, the waveform area shows the error message
        /// and waveform generation is skipped (already handled in AudioPreviewHandler).
        /// </summary>
        public void UpdateContent(AudioFileInfo info)
        {
            CancelWaveformGeneration();
            CancelBpmKeyAnalysis();

            // Close settings overlay on file switch (pitfall: stale overlay state)
            CloseSettings();

            _currentFileInfo = info;
            _showError = false;
            _errorMessage = null;
            _formatError = info.FormatError; // May be null (normal) or non-null (format unsupported)

            // Reset waveform state for new file
            _waveformPeaks = null;
            _waveformBarsReady = 0;
            _waveformUnavailable = false;
            _isWaveformDragging = false;
            _isHoveringWaveform = false;
            _waveformColors = null;
            _isToggleHovered = false;
            _isTogglePressed = false;
            _waveformToggleBounds = Rectangle.Empty;
            _controlBarLayout = ControlBarRenderer.ControlBarLayout.Empty;
            // _isWaveformColorMode is NOT reset — it persists per user preference

            // Reset analysis state for new file
            _analysisResult = null;
            _isAnalyzing = false;
            _analysisProgress = 0f;
            _isReanalyzing = false;
            _isReanalyzeHovered = false;
            _reanalyzeButtonBounds = Rectangle.Empty;
            _currentAudioData = null;
            _currentCacheKey = null;
            _isModuleFormat = false;
            _currentDuration = 0;
            _hasBpmTag = false;
            _hasKeyTag = false;

            StopLoading();
            Invalidate();
        }

        /// <summary>
        /// Shows a full error panel with the given message.
        /// </summary>
        public void ShowError(string message)
        {
            _showError = true;
            _errorMessage = message;
            Invalidate();
        }

        // -------------------------------------------------------------------------
        // Settings overlay public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Gets whether the settings overlay is currently open.
        /// </summary>
        public bool IsSettingsOpen => _settingsOpen;

        /// <summary>
        /// Opens the settings overlay and enumerates WASAPI output devices.
        /// </summary>
        public void OpenSettings()
        {
            _settingsOpen = true;
            _isDeviceDropdownOpen = false;
            _isKeyProfileDropdownOpen = false;
            _settingsOverlayLayout = SettingsOverlayLayout.Empty;
            _settingsOverlayBounds = SettingsOverlayRenderer.GetOverlayBounds(ClientRectangle, _dpiScale);
            _settingsOverlayLayout.OverlayBounds = _settingsOverlayBounds;

            // Clear any visible tooltip — it cannot show over the settings overlay
            _tooltipTimer?.Stop();
            _tooltipVisible = false;
            _tooltipText = null;

            // Enumerate WASAPI devices once on open
            if (_player != null)
            {
                try { _wasapiDevices = _player.GetWasapiOutputDevices(); }
                catch { _wasapiDevices = new List<(int, string)> { (-1, "Default Output Device") }; }
            }
            else
            {
                _wasapiDevices = new List<(int, string)> { (-1, "Default Output Device") };
            }

            // Find current selection in device list
            _selectedDeviceDropdownIndex = FindSelectedDeviceIndex();

            Invalidate();
        }

        /// <summary>
        /// Closes the settings overlay and resets dropdown state.
        /// </summary>
        public void CloseSettings()
        {
            if (!_settingsOpen) return;
            _settingsOpen = false;
            _isDeviceDropdownOpen = false;
            _isKeyProfileDropdownOpen = false;
            _settingsOverlayLayout = SettingsOverlayLayout.Empty;
            Invalidate();
        }

        /// <summary>
        /// Toggles the settings overlay open/closed.
        /// </summary>
        public void ToggleSettings()
        {
            if (_settingsOpen)
                CloseSettings();
            else
                OpenSettings();
        }

        private void HandleSettingsOverlayClick(Point location)
        {
            SettingsHitZone zone = SettingsOverlayRenderer.HitTest(_settingsOverlayLayout, location);

            switch (zone)
            {
                case SettingsHitZone.None:
                    // Click outside overlay — dismiss
                    CloseSettings();
                    break;

                case SettingsHitZone.CloseButton:
                    CloseSettings();
                    break;

                case SettingsHitZone.Background:
                    // Click inside overlay but on no control — do nothing (prevent click-through)
                    if (_isDeviceDropdownOpen || _isKeyProfileDropdownOpen)
                    {
                        _isDeviceDropdownOpen = false;
                        _isKeyProfileDropdownOpen = false;
                        Invalidate();
                    }
                    break;

                case SettingsHitZone.DeviceSelector:
                    _isDeviceDropdownOpen = !_isDeviceDropdownOpen;
                    _isKeyProfileDropdownOpen = false;
                    Invalidate();
                    break;

                case SettingsHitZone.DeviceDropdownItem:
                {
                    int itemIdx = SettingsOverlayRenderer.GetDeviceDropdownItemIndex(_settingsOverlayLayout, location);
                    if (itemIdx >= 0 && _wasapiDevices != null && itemIdx < _wasapiDevices.Count)
                    {
                        _selectedDeviceDropdownIndex = itemIdx;
                        int deviceIdx = _wasapiDevices[itemIdx].Index;
                        SaveConfig(cfg => cfg.WasapiDeviceIndex = deviceIdx);
                        _isDeviceDropdownOpen = false;
                        _isKeyProfileDropdownOpen = false;
                        Invalidate();
                    }
                    break;
                }

                case SettingsHitZone.KeyProfileSelector:
                    _isKeyProfileDropdownOpen = !_isKeyProfileDropdownOpen;
                    _isDeviceDropdownOpen = false;
                    Invalidate();
                    break;

                case SettingsHitZone.KeyProfileDropdownItem:
                {
                    int itemIdx = SettingsOverlayRenderer.GetKeyProfileDropdownItemIndex(_settingsOverlayLayout, location);
                    string profile = SettingsOverlayRenderer.GetKeyProfileValueByIndex(itemIdx);
                    if (!string.Equals(profile, NormalizeKeyProfile(_config.KeyDetectionProfile), StringComparison.Ordinal))
                    {
                        SaveConfig(cfg => cfg.KeyDetectionProfile = profile);

                        // Re-run analysis with the newly selected profile (cache key includes profile).
                        if (_currentAudioData != null)
                        {
                            StartBpmKeyAnalysis(_currentAudioData, _isModuleFormat, _currentDuration, _hasBpmTag, _hasKeyTag);
                        }
                    }
                    _isKeyProfileDropdownOpen = false;
                    Invalidate();
                    break;
                }

                case SettingsHitZone.FrequencyColorToggle:
                {
                    _isWaveformColorMode = !_isWaveformColorMode;
                    SaveConfig(cfg => cfg.WaveformColorMode = _isWaveformColorMode);
                    Invalidate();
                    break;
                }

                case SettingsHitZone.HeightPresetSmall:
                    SetWaveformHeightPreset("Small");
                    break;

                case SettingsHitZone.HeightPresetMedium:
                    SetWaveformHeightPreset("Medium");
                    break;

                case SettingsHitZone.HeightPresetLarge:
                    SetWaveformHeightPreset("Large");
                    break;

                case SettingsHitZone.AnalysisToggle:
                {
                    bool newEnabled;
                    newEnabled = !_config.EnableBpmKeyDetection;
                    SaveConfig(cfg => cfg.EnableBpmKeyDetection = newEnabled);
                    // If toggled ON and a file is loaded, re-trigger analysis (cache check runs first inside)
                    if (newEnabled && _currentAudioData != null)
                    {
                        StartBpmKeyAnalysis(_currentAudioData, _isModuleFormat, _currentDuration, false, false);
                    }
                    Invalidate();
                    break;
                }

                case SettingsHitZone.ClearCacheButton:
                    try { AnalysisCache.ClearAll(); } catch { }
                    Invalidate();
                    break;

                case SettingsHitZone.CheckUpdatesButton:
                    CheckForUpdatesAsync();
                    break;

                case SettingsHitZone.ResetDefaultsButton:
                    ResetToDefaults();
                    break;
            }
        }

        private void SetWaveformHeightPreset(string preset)
        {
            _waveformHeightPreset = preset;
            SaveConfig(cfg => cfg.WaveformHeightPreset = preset);
            Invalidate();
        }

        private void CheckForUpdatesAsync()
        {
            System.Threading.Thread bgThread = new System.Threading.Thread(() =>
            {
                try
                {
                    const string url = "https://api.github.com/repos/barretts/Audex/releases/latest";
                    string json;
                    using (var client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("Audex/1.0");
                        json = client.GetStringAsync(url).GetAwaiter().GetResult();
                    }

                    var obj = JObject.Parse(json);
                    string tagName = obj["tag_name"]?.ToString() ?? "";
                    string latestVersion = string.IsNullOrEmpty(tagName) ? "unknown" : tagName;

                    if (!IsHandleCreated || IsDisposed) return;
                    try
                    {
                        Invoke(new Action(() =>
                        {
                            MessageBox.Show(
                                $"Latest version: {latestVersion}\n\nVisit GitHub releases to download updates.",
                                "Check for Updates",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }));
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    try
                    {
                        Invoke(new Action(() =>
                        {
                            MessageBox.Show(
                                $"Could not check for updates: {ex.Message}",
                                "Check for Updates",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }));
                    }
                    catch { }
                }
            });
            bgThread.IsBackground = true;
            bgThread.Start();
        }

        private void ResetToDefaults()
        {
            var defaults = new AppConfig();
            _config = defaults;
            ConfigManager.Save(_config);

            // Reload local state from defaults
            _volume = defaults.Volume;
            _isMuted = defaults.IsMuted;
            _isWaveformColorMode = defaults.WaveformColorMode;
            _isAutoplay = defaults.Autoplay;
            _isLoop = defaults.Loop;
            _waveformHeightPreset = defaults.WaveformHeightPreset;
            _selectedDeviceDropdownIndex = 0;
            _isDeviceDropdownOpen = false;
            _isKeyProfileDropdownOpen = false;

            // Apply to player
            if (_player != null)
            {
                try
                {
                    _player.SetVolume(_volume);
                    _player.SetMute(_isMuted);
                }
                catch { }
            }

            Invalidate();
        }

        private int FindSelectedDeviceIndex()
        {
            if (_wasapiDevices == null) return 0;
            int deviceIdx = _config.WasapiDeviceIndex;
            for (int i = 0; i < _wasapiDevices.Count; i++)
            {
                if (_wasapiDevices[i].Index == deviceIdx)
                    return i;
            }
            return 0; // default to first (Default Output Device)
        }

        /// <summary>
        /// Returns the waveform height in pixels for the current DPI scale and preset.
        /// Small = 80px, Medium = 120px (default), Large = 160px (all DPI-scaled).
        /// </summary>
        public int GetWaveformHeight(float dpiScale)
        {
            switch (_waveformHeightPreset)
            {
                case "Small":  return (int)(80 * dpiScale);
                case "Large":  return (int)(160 * dpiScale);
                default:       return (int)(120 * dpiScale); // "Medium"
            }
        }

        // -------------------------------------------------------------------------
        // Loading spinner
        // -------------------------------------------------------------------------

        /// <summary>
        /// Starts the loading state with delayed spinner.
        /// </summary>
        public void StartLoading()
        {
            _isLoading = true;

            _loadingDelayTimer?.Dispose();
            _loadingDelayTimer = new Timer { Interval = LOADING_DELAY_MS };
            _loadingDelayTimer.Tick += (s, e) =>
            {
                _loadingDelayTimer?.Stop();
                _loadingDelayTimer?.Dispose();
                _loadingDelayTimer = null;

                if (_isLoading)
                {
                    _spinnerTimer?.Dispose();
                    _spinnerTimer = new Timer { Interval = SPINNER_INTERVAL_MS };
                    _spinnerTimer.Tick += (s2, e2) =>
                    {
                        _spinnerFrame++;
                        Invalidate();
                    };
                    _spinnerTimer.Start();
                    Invalidate();
                }
            };
            _loadingDelayTimer.Start();
        }

        /// <summary>
        /// Stops the loading state.
        /// </summary>
        public void StopLoading()
        {
            _isLoading = false;
            _spinnerTimer?.Stop();
            _spinnerTimer?.Dispose();
            _spinnerTimer = null;
            _loadingDelayTimer?.Stop();
            _loadingDelayTimer?.Dispose();
            _loadingDelayTimer = null;
        }

        // -------------------------------------------------------------------------
        // Waveform lifecycle
        // -------------------------------------------------------------------------

        /// <summary>
        /// Starts background waveform generation for the given audio data.
        /// Cancels any in-progress generation first. Checks cache before generating.
        /// </summary>
        public void StartWaveformGeneration(byte[] audioData, double totalDuration, bool isModuleFormat = false)
        {
            // Cancel any in-progress generation
            _waveCts?.Cancel();
            _waveCts?.Dispose();
            _waveCts = null;

            // Reset waveform state
            _waveformPeaks = null;
            _waveformBarsReady = 0;
            _waveformUnavailable = false;

            // Increment generation ID to invalidate stale callbacks
            int generationId = System.Threading.Interlocked.Increment(ref _currentGenerationId);

            // Check cache first (peaks + colors)
            string key = WaveformCache.ComputeCacheKey(audioData);
            float[]? cached = WaveformCache.ReadCache(key);
            Color[]? cachedColors = WaveformCache.ReadColorCache(key);
            if (cached != null && (isModuleFormat || cachedColors != null))
            {
                // Module waveforms only cache peaks. Non-module waveforms require both peaks and colors.
                _waveformPeaks = cached;
                _waveformBarsReady = cached.Length;
                _waveformColors = cachedColors;
                Invalidate(_waveformBounds);
                return;
            }
            else if (cached != null)
            {
                // Peaks cached but no colors — show peaks immediately, generate colors in background
                _waveformPeaks = cached;
                _waveformBarsReady = cached.Length;
                Invalidate(_waveformBounds);
                // Fall through to background generation for colors
            }

            // Create new cancellation token source
            _waveCts = new System.Threading.CancellationTokenSource();
            System.Threading.CancellationToken ct = _waveCts.Token;

            // Capture locals for closure
            byte[] audioDataRef = audioData;
            string cacheKey = key;
            bool isModule = isModuleFormat;

            // Start background thread
            System.Threading.Thread bgThread = new System.Threading.Thread(() =>
            {
                // Pre-allocate peaks array on UI thread before starting; share the reference
                float[] localPeaks = new float[2000];

                // Batch progressive reveal: accumulate bars and invoke every ~50 bars
                const int batchSize = 50;
                int batchCount = 0;
                int lastInvokedBar = -1;

                Action<int, float> onBarReady = (barIndex, peak) =>
                {
                    if (ct.IsCancellationRequested) return;
                    if (barIndex < localPeaks.Length)
                        localPeaks[barIndex] = peak;

                    batchCount++;
                    if (batchCount >= batchSize)
                    {
                        batchCount = 0;
                        int capturedBar = barIndex;
                        if (!IsHandleCreated || IsDisposed) return;
                        try
                        {
                            Invoke(new Action(() =>
                            {
                                if (_currentGenerationId != generationId) return;
                                // Copy the batch into the shared peaks array
                                if (_waveformPeaks == null)
                                    _waveformPeaks = localPeaks;
                                _waveformBarsReady = capturedBar + 1;
                                Invalidate(_waveformBounds);
                            }));
                        }
                        catch { }
                        lastInvokedBar = barIndex;
                    }
                };

                WaveformData? result = WaveformGenerator.Generate(audioDataRef, ct, isModule, onBarReady);

                if (ct.IsCancellationRequested)
                    return;

                // Generation complete (result may be null on failure)
                if (result != null)
                {
                    float[] peaks = result.Peaks;

                    // Write peaks cache
                    try { WaveformCache.WriteCache(cacheKey, peaks); } catch { }
                    // Write color cache
                    if (result.FrequencyColors != null)
                    {
                        try { WaveformCache.WriteColorCache(cacheKey, result.FrequencyColors); } catch { }
                    }

                    if (!IsHandleCreated || IsDisposed) return;
                    try
                    {
                        Invoke(new Action(() =>
                        {
                            if (_currentGenerationId != generationId) return;
                            _waveformPeaks = peaks;
                            _waveformBarsReady = peaks.Length;
                            _waveformColors = result.FrequencyColors;
                            Invalidate(_waveformBounds);
                        }));
                    }
                    catch { }
                }
                else
                {
                    // Generation failed (not cancelled)
                    if (!IsHandleCreated || IsDisposed) return;
                    try
                    {
                        Invoke(new Action(() =>
                        {
                            if (_currentGenerationId != generationId) return;
                            _waveformUnavailable = true;
                            Invalidate(_waveformBounds);
                        }));
                    }
                    catch { }
                }
            });
            bgThread.IsBackground = true;
            bgThread.Start();
        }

        /// <summary>
        /// Cancels any in-progress waveform generation.
        /// </summary>
        public void CancelWaveformGeneration()
        {
            System.Threading.Interlocked.Increment(ref _currentGenerationId);
            _waveCts?.Cancel();
            _waveCts?.Dispose();
            _waveCts = null;
        }

        // -------------------------------------------------------------------------
        // Analysis lifecycle
        // -------------------------------------------------------------------------

        /// <summary>
        /// Starts background BPM/key analysis for the given audio data.
        /// Cancels any in-progress analysis first. Checks cache before analyzing.
        /// Mirrors StartWaveformGeneration pattern: analysisId guard, CancellationToken, Invoke to UI thread.
        /// </summary>
        /// <param name="audioData">Raw audio file bytes</param>
        /// <param name="isModuleFormat">True for .mod/.xm/.it/.s3m (skips analysis)</param>
        /// <param name="duration">File duration in seconds (files under 5s are skipped)</param>
        /// <param name="hasBpmTag">True if file already has BPM from tags</param>
        /// <param name="hasKeyTag">True if file already has Key from tags</param>
        public void StartBpmKeyAnalysis(byte[] audioData, bool isModuleFormat, double duration,
            bool hasBpmTag, bool hasKeyTag)
        {
            // Cancel any in-progress analysis
            _analysisCts?.Cancel();
            _analysisCts?.Dispose();
            _analysisCts = null;

            // Store tag presence
            _hasBpmTag = hasBpmTag;
            _hasKeyTag = hasKeyTag;

            // Store reference for re-analyze support
            _currentAudioData = audioData;
            _isModuleFormat = isModuleFormat;
            _currentDuration = duration;

            // Skip analysis when both tags present, module format, or too short
            if ((hasBpmTag && hasKeyTag) || isModuleFormat || duration < 5.0)
                return;

            // Compute cache key (include key-profile strategy to avoid cross-profile cache pollution)
            string keyProfile = NormalizeKeyProfile(_config.KeyDetectionProfile);
            string cacheKey = BuildAnalysisCacheKey(audioData, keyProfile);
            _currentCacheKey = cacheKey;

            // Check cache first — instant display if hit (regardless of toggle state)
            AnalysisResult? cached = AnalysisCache.Read(cacheKey);
            if (cached != null)
            {
                _analysisResult = cached;
                _isAnalyzing = false;
                Invalidate(_metadataBounds);
                return;
            }

            // Check config toggle — gate only live analysis, not cache reads above
            if (!_config.EnableBpmKeyDetection)
                return;

            // Start fresh analysis
            _isAnalyzing = true;
            _analysisProgress = 0f;
            // Don't clear _analysisResult here — if reanalyzing, keep old result visible (dimmed)
            if (!_isReanalyzing)
                _analysisResult = null;

            // Increment analysis ID to guard against stale callbacks
            int analysisId = System.Threading.Interlocked.Increment(ref _currentAnalysisId);

            // Create new cancellation token source
            _analysisCts = new System.Threading.CancellationTokenSource();
            System.Threading.CancellationToken ct = _analysisCts.Token;

            // Capture locals for the background thread closure
            byte[] audioDataRef = audioData;
            string capturedCacheKey = cacheKey;
            string capturedKeyProfile = keyProfile;

            System.Threading.Thread bgThread = new System.Threading.Thread(() =>
            {
                // Wait ANALYSIS_DELAY_MS before starting (cancellable delay)
                bool cancelled = ct.WaitHandle.WaitOne(ANALYSIS_DELAY_MS);
                if (cancelled || ct.IsCancellationRequested) return;

                // Progress callback: batch UI updates (only when change >= 2%)
                float lastReportedProgress = 0f;
                Action<float> onProgress = (progress) =>
                {
                    if (ct.IsCancellationRequested) return;
                    if (progress - lastReportedProgress < 0.02f && progress < 0.99f) return;
                    lastReportedProgress = progress;

                    if (!IsHandleCreated || IsDisposed) return;
                    try
                    {
                        Invoke(new Action(() =>
                        {
                            if (_currentAnalysisId != analysisId) return;
                            _analysisProgress = progress;
                            Invalidate(_metadataBounds);
                        }));
                    }
                    catch { }
                };

                // Run analysis (BPM + key phases)
                AnalysisResult? result = BpmKeyAnalyzer.Analyze(
                    audioDataRef, ct, onProgress, 300.0, capturedKeyProfile);

                if (ct.IsCancellationRequested || result == null) return;

                // Cache even failures (so we don't re-analyze repeatedly)
                try { AnalysisCache.Write(capturedCacheKey, result); } catch { }

                // Marshal result to UI thread
                if (!IsHandleCreated || IsDisposed) return;
                try
                {
                    Invoke(new Action(() =>
                    {
                        if (_currentAnalysisId != analysisId) return;
                        _analysisResult = result;
                        _isAnalyzing = false;
                        _isReanalyzing = false;
                        Invalidate(_metadataBounds);
                    }));
                }
                catch { }
            });
            bgThread.IsBackground = true;
            bgThread.Start();
        }

        /// <summary>
        /// Cancels any in-progress BPM/key analysis.
        /// </summary>
        public void CancelBpmKeyAnalysis()
        {
            System.Threading.Interlocked.Increment(ref _currentAnalysisId);
            _analysisCts?.Cancel();
            _analysisCts?.Dispose();
            _analysisCts = null;
            _isAnalyzing = false;
            _analysisProgress = 0f;
        }

        // -------------------------------------------------------------------------
        // Rendering
        // -------------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;

            try
            {
                g.Clear(ThemeHelper.GetBackgroundColor());
                _controlBarLayout = ControlBarRenderer.ControlBarLayout.Empty;
                _waveformToggleBounds = Rectangle.Empty;
                _reanalyzeButtonBounds = Rectangle.Empty;
                _settingsOverlayLayout = SettingsOverlayLayout.Empty;

                if (_isLoading && _spinnerTimer != null)
                {
                    DrawLoadingSpinner(g, ClientRectangle);
                }
                else if (_currentFileInfo != null)
                {
                    float dpiScale = g.DpiX / 96.0f;
                    _dpiScale = dpiScale; // Cache for event handlers (avoids CreateGraphics() GDI leaks)
                    int controlBarHeight = ControlBarRenderer.GetControlBarHeight(dpiScale);

                    // Waveform area: fixed height preset (Small/Medium/Large), does not scale with pane resize
                    int waveformHeight = GetWaveformHeight(dpiScale);
                    int waveformTop = ClientRectangle.Bottom - controlBarHeight - waveformHeight;

                    // If not enough room for metadata + waveform + control bar, collapse metadata
                    int metadataHeight = waveformTop - ClientRectangle.Y;
                    if (metadataHeight < (int)(30 * dpiScale))
                    {
                        metadataHeight = 0;
                        waveformTop = ClientRectangle.Y;
                        waveformHeight = ClientRectangle.Height - controlBarHeight;
                    }

                    Rectangle metadataBounds = new Rectangle(
                        ClientRectangle.X, ClientRectangle.Y,
                        ClientRectangle.Width, metadataHeight);
                    _metadataBounds = metadataBounds;

                    Rectangle waveformBounds = new Rectangle(
                        ClientRectangle.X, waveformTop,
                        ClientRectangle.Width, waveformHeight);
                    _waveformBounds = waveformBounds;

                    Rectangle controlBarBounds = new Rectangle(
                        ClientRectangle.X, ClientRectangle.Bottom - controlBarHeight,
                        ClientRectangle.Width, controlBarHeight);

                    // Render metadata area (only if there's enough room)
                    if (metadataHeight > 0)
                    {
                        LayoutRenderer.Render(g, metadataBounds, _currentFileInfo, _showError, _errorMessage,
                            _analysisResult, _isAnalyzing, _analysisProgress, _isReanalyzing, _isReanalyzeHovered,
                            out _reanalyzeButtonBounds);
                    }

                    // Render waveform area
                    double position = _player?.CurrentPositionSeconds ?? 0;
                    double duration = _player?.TotalDurationSeconds ?? (_currentFileInfo?.Duration ?? 0);

                    if (_formatError != null)
                    {
                        // Format cannot be decoded — show error message in waveform area instead
                        WaveformRenderer.DrawFormatError(g, waveformBounds, _formatError, dpiScale);
                    }
                    else
                    {
                        _waveformToggleBounds = WaveformRenderer.Draw(g, waveformBounds, _waveformPeaks, _waveformBarsReady,
                            _waveformColors, _isWaveformColorMode,
                            position, duration, dpiScale,
                            _isHoveringWaveform, _waveformHoverPoint, _isWaveformDragging, _waveformDragPosition,
                            _waveformUnavailable,
                            _isToggleHovered, _isTogglePressed);
                    }

                    // Render control bar
                    AudioPlayerState state = _player?.State ?? AudioPlayerState.Idle;

                    _controlBarLayout = ControlBarRenderer.Draw(g, controlBarBounds, state, position, duration,
                        _volume, _isMuted, _hoveredZone, _pressedZone, dpiScale,
                        _isAutoplay, _isLoop);

                    // Draw gear icon in top-right corner
                    DrawGearIcon(g, dpiScale);

                    // Draw settings overlay LAST (on top of everything)
                    if (_settingsOpen)
                    {
                        bool isDark = ThemeHelper.IsSystemInDarkMode();
                        _settingsOverlayBounds = SettingsOverlayRenderer.GetOverlayBounds(ClientRectangle, dpiScale);
                        _settingsOverlayLayout = SettingsOverlayRenderer.Draw(g, _settingsOverlayBounds, _config,
                            _wasapiDevices, isDark, _selectedDeviceDropdownIndex, _isDeviceDropdownOpen,
                            _isKeyProfileDropdownOpen,
                            _waveformHeightPreset);
                    }

                    // Draw owner-drawn tooltip (last, on top of everything except settings overlay)
                    if (_tooltipVisible && !string.IsNullOrEmpty(_tooltipText) && !_settingsOpen)
                    {
                        DrawOwnerTooltip(g, dpiScale);
                    }
                }
            }
            catch (Exception ex)
            {
                // Catch GDI+ and other rendering exceptions to prevent WinForms from
                // permanently stopping WM_PAINT messages (which causes a white/blank control).
                System.Diagnostics.Debug.WriteLine($"[PW] OnPaint exception: {ex.Message}");
                try { Logger.Error($"[PW] OnPaint exception: {ex.Message}", ex); } catch { }
            }
        }

        private void DrawOwnerTooltip(Graphics g, float dpiScale)
        {
            bool isDark = ThemeHelper.IsSystemInDarkMode();

            using (Font tipFont = new Font("Segoe UI", 9f * dpiScale, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                SizeF textSize = g.MeasureString(_tooltipText, tipFont);
                int padX = (int)(6 * dpiScale);
                int padY = (int)(4 * dpiScale);
                int tipW = (int)textSize.Width + padX * 2;
                int tipH = (int)textSize.Height + padY * 2;

                // Position: above the mouse cursor, horizontally centered on cursor
                int tipX = _tooltipPosition.X - tipW / 2;
                int tipY = _tooltipPosition.Y - tipH - (int)(8 * dpiScale);

                // Clamp to control bounds
                tipX = Math.Max(2, Math.Min(tipX, ClientRectangle.Width - tipW - 2));
                tipY = Math.Max(2, tipY);

                // If would be above the control, show below cursor instead
                if (tipY < 2)
                    tipY = _tooltipPosition.Y + (int)(20 * dpiScale);

                Rectangle tipRect = new Rectangle(tipX, tipY, tipW, tipH);

                // Background: dark theme = light tooltip, light theme = dark tooltip (high contrast)
                Color bgColor = isDark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(50, 50, 50);
                Color textColor = isDark ? Color.FromArgb(30, 30, 30) : Color.FromArgb(245, 245, 245);
                Color borderColor = isDark ? Color.FromArgb(180, 180, 180) : Color.FromArgb(100, 100, 100);

                using (Brush bg = new SolidBrush(bgColor))
                using (Pen border = new Pen(borderColor, 1f))
                using (Brush text = new SolidBrush(textColor))
                {
                    g.FillRectangle(bg, tipRect);
                    g.DrawRectangle(border, tipRect);
                    g.DrawString(_tooltipText, tipFont, text,
                        tipRect.X + padX, tipRect.Y + padY);
                }
            }
        }

        private void DrawGearIcon(Graphics g, float dpiScale)
        {
            int iconSize = (int)(18 * dpiScale);
            int pad = (int)(6 * dpiScale);
            _gearIconRect = new Rectangle(
                ClientRectangle.Right - pad - iconSize,
                ClientRectangle.Top + pad,
                iconSize, iconSize);

            // Hover background
            if (_isGearHovered || _settingsOpen)
            {
                bool isDark = ThemeHelper.IsSystemInDarkMode();
                Color hoverBg = ThemeHelper.SettingsOverlayButtonHover(isDark);
                using (Brush hoverBrush = new SolidBrush(hoverBg))
                {
                    int bgPad = (int)(2 * dpiScale);
                    g.FillRectangle(hoverBrush,
                        _gearIconRect.X - bgPad, _gearIconRect.Y - bgPad,
                        _gearIconRect.Width + bgPad * 2, _gearIconRect.Height + bgPad * 2);
                }
            }

            // Gear glyph using Segoe UI Symbol
            Color iconColor = ThemeHelper.GetSecondaryTextColor();
            try
            {
                using (Font gearFont = new Font("Segoe UI Symbol", iconSize * 0.85f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (Brush iconBrush = new SolidBrush(iconColor))
                using (StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    g.DrawString("\u2699", gearFont, iconBrush, _gearIconRect, sf);
                }
            }
            catch
            {
                // Fallback: draw a simple circle with spokes
                var oldMode = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen iconPen = new Pen(iconColor, Math.Max(1f, dpiScale)))
                {
                    g.DrawEllipse(iconPen,
                        _gearIconRect.X + _gearIconRect.Width / 4,
                        _gearIconRect.Y + _gearIconRect.Height / 4,
                        _gearIconRect.Width / 2, _gearIconRect.Height / 2);
                }
                g.SmoothingMode = oldMode;
            }
        }

        private void DrawLoadingSpinner(Graphics g, Rectangle bounds)
        {
            string[] spinnerFrames = { "\u280B", "\u2819", "\u2839", "\u2838", "\u283C", "\u2834", "\u2826", "\u2827", "\u2807", "\u280F" };
            string spinnerText = spinnerFrames[_spinnerFrame % spinnerFrames.Length];

            using (Font font = new Font("Segoe UI", 24.0f * (g.DpiX / 96.0f)))
            using (Brush textBrush = new SolidBrush(ThemeHelper.GetTextColor()))
            using (StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                g.DrawString(spinnerText + " Loading...", font, textBrush, bounds, format);
            }
        }

        // -------------------------------------------------------------------------
        // Position timer
        // -------------------------------------------------------------------------

        private void OnPositionTimerTick(object? sender, EventArgs e)
        {
            if (_player == null) return;

            // Check for track end (atomically clears end-of-stream flag)
            _player.CheckEndOfStream();

            // Invalidate both the waveform bounds and control bar area to update playhead + seek bar
            if (_player.State == AudioPlayerState.Playing)
            {
                float dpiScale = _dpiScale;
                int controlBarHeight = ControlBarRenderer.GetControlBarHeight(dpiScale);
                Rectangle controlBarBounds = new Rectangle(
                    ClientRectangle.X,
                    ClientRectangle.Bottom - controlBarHeight,
                    ClientRectangle.Width,
                    controlBarHeight);
                Invalidate(_waveformBounds);
                Invalidate(controlBarBounds);
            }
        }

        private void StartPositionTimer()
        {
            _positionTimer?.Start();
        }

        private void StopPositionTimer()
        {
            _positionTimer?.Stop();
        }

        // -------------------------------------------------------------------------
        // AudioPlayer event handlers
        // -------------------------------------------------------------------------

        private void OnPlayerStateChanged(object? sender, AudioPlayerState newState)
        {
            // StateChanged fires from audio thread or UI timer; always marshal to STA
            if (InvokeRequired)
            {
                try { Invoke(new Action(() => OnPlayerStateChanged(sender, newState))); } catch { }
                return;
            }

            if (newState == AudioPlayerState.Playing)
                StartPositionTimer();
            else
                StopPositionTimer();

            Invalidate();
        }

        private void OnPlaybackEnded(object? sender, EventArgs e)
        {
            // Fires from UI timer (CheckEndOfStream) — should be on STA already
            if (InvokeRequired)
            {
                try { Invoke(new Action(() => OnPlaybackEnded(sender, e))); } catch { }
                return;
            }

            if (_isLoop && _player != null)
            {
                // Loop: seek to start and play again
                try
                {
                    _player.Seek(0);
                    _player.Play();
                    return; // do NOT stop timer — playback continues
                }
                catch { }
            }

            StopPositionTimer();
            Invalidate();
        }

        // -------------------------------------------------------------------------
        // Mouse event handlers
        // -------------------------------------------------------------------------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            // Update gear icon hover state
            bool overGear = _gearIconRect.Contains(e.Location);
            if (overGear != _isGearHovered)
            {
                _isGearHovered = overGear;
                if (overGear)
                    Cursor = Cursors.Hand;
                Invalidate(_gearIconRect);
            }

            // When overlay is open, consume mouse move events inside overlay
            if (_settingsOpen && _settingsOverlayLayout.OverlayBounds.Contains(e.Location))
            {
                Cursor = Cursors.Default;
                return; // Don't update hover states for controls beneath overlay
            }

            float dpiScale = _dpiScale;
            int controlBarHeight = ControlBarRenderer.GetControlBarHeight(dpiScale);
            Rectangle controlBarBounds = new Rectangle(
                ClientRectangle.X,
                ClientRectangle.Bottom - controlBarHeight,
                ClientRectangle.Width,
                controlBarHeight);

            // Handle waveform drag (while mouse button is down)
            if (e.Button == MouseButtons.Left && _isWaveformDragging)
            {
                if (_player != null && _player.TotalDurationSeconds > 0)
                {
                    double ratio = GetWaveformTimeRatio(e.Location);
                    _waveformDragPosition = ratio * _player.TotalDurationSeconds;
                }
                Invalidate(_waveformBounds);
                return;
            }

            // Check re-analyze button hover (in metadata area)
            bool overReanalyze = LayoutRenderer.HitTestReanalyze(_reanalyzeButtonBounds, e.Location);
            if (overReanalyze != _isReanalyzeHovered)
            {
                _isReanalyzeHovered = overReanalyze;
                Cursor = overReanalyze ? Cursors.Hand : Cursors.Default;
                Invalidate(_metadataBounds);
            }

            // Check if over waveform area
            bool overWaveform = WaveformRenderer.HitTest(e.Location, _waveformBounds);
            if (overWaveform)
            {
                // Check if over toggle button specifically
                bool overToggle = WaveformRenderer.HitTestToggle(e.Location, _waveformToggleBounds);
                if (overToggle != _isToggleHovered)
                {
                    _isToggleHovered = overToggle;
                    Invalidate(_waveformBounds);
                }

                // Set cursor: hand for toggle, cross for waveform
                Cursor = overToggle ? Cursors.Hand : Cursors.Cross;

                // Don't show guide line when over toggle
                _isHoveringWaveform = !overToggle;
                _waveformHoverPoint = e.Location;
                Invalidate(_waveformBounds); // always redraw for guide line movement
                return;
            }

            // Not over waveform — clear toggle hover
            if (_isToggleHovered)
            {
                _isToggleHovered = false;
                Invalidate(_waveformBounds);
            }

            if (_isHoveringWaveform)
            {
                _isHoveringWaveform = false;
                if (!overReanalyze)
                    Cursor = Cursors.Default;
                Invalidate(_waveformBounds);
            }

            // Handle control bar drag operations while mouse is down
            if (e.Button == MouseButtons.Left)
            {
                if (_isSeeking && _player != null && _player.TotalDurationSeconds > 0)
                {
                    double ratio = GetSeekRatio(e.Location, controlBarBounds, dpiScale);
                    _player.Seek(ratio * _player.TotalDurationSeconds);
                    Invalidate(controlBarBounds);
                    return;
                }

                if (_pressedZone == HitZone.VolumeSlider)
                {
                    SetVolumeFromPoint(e.Location, controlBarBounds, dpiScale);
                    Invalidate(controlBarBounds);
                    return;
                }
            }

            HitZone newHovered = ControlBarRenderer.HitTest(e.Location, _controlBarLayout, dpiScale);

            if (newHovered != _hoveredZone)
            {
                _hoveredZone = newHovered;
                if (!overReanalyze)
                    Cursor = (newHovered != HitZone.None) ? Cursors.Hand : Cursors.Default;
                Invalidate(controlBarBounds);
            }

            // Update owner-drawn tooltip based on current hover position
            UpdateTooltipForPosition(e.Location, overGear);
        }

        private void UpdateTooltipForPosition(Point mousePos, bool overGear)
        {
            string? newTip = null;
            if (overGear)
            {
                newTip = "Settings (Ctrl+,)";
            }
            else
            {
                newTip = ControlBarRenderer.GetTooltipText(_hoveredZone);
            }

            // Same tooltip text — no change needed
            if (newTip == _tooltipText && _tooltipText != null) return;

            // Tooltip changed
            _tooltipTimer?.Stop();
            _tooltipVisible = false;

            if (newTip != null)
            {
                _tooltipText = newTip;
                _tooltipPosition = mousePos;
                _tooltipTimer?.Start();
            }
            else
            {
                if (_tooltipText != null)
                {
                    _tooltipText = null;
                    Invalidate(); // clear visible tooltip
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            // Check gear icon hit FIRST (always active when file is loaded)
            if (_gearIconRect.Contains(e.Location))
            {
                ToggleSettings();
                return;
            }

            // If settings overlay is open, handle overlay interactions
            if (_settingsOpen)
            {
                HandleSettingsOverlayClick(e.Location);
                return;
            }

            float dpiScale = _dpiScale;
            int controlBarHeight = ControlBarRenderer.GetControlBarHeight(dpiScale);
            Rectangle controlBarBounds = new Rectangle(
                ClientRectangle.X,
                ClientRectangle.Bottom - controlBarHeight,
                ClientRectangle.Width,
                controlBarHeight);

            // Check re-analyze button hit FIRST
            if (LayoutRenderer.HitTestReanalyze(_reanalyzeButtonBounds, e.Location))
            {
                // Check cooldown
                if (DateTime.Now - _lastReanalyzeTime > TimeSpan.FromMilliseconds(REANALYZE_COOLDOWN_MS)
                    && _currentAudioData != null && _currentCacheKey != null)
                {
                    _lastReanalyzeTime = DateTime.Now;

                    // Delete cached entry so fresh analysis runs
                    try { AnalysisCache.Delete(_currentCacheKey); } catch { }

                    // Set re-analyzing state (keeps old result visible dimmed)
                    _isReanalyzing = true;

                    // Restart analysis
                    StartBpmKeyAnalysis(_currentAudioData, _isModuleFormat, _currentDuration,
                        _hasBpmTag, _hasKeyTag);

                    Invalidate(_metadataBounds);
                }
                return; // Consume the click
            }

            // Check toggle button hit FIRST (prevent click-through to waveform seek)
            if (WaveformRenderer.HitTestToggle(e.Location, _waveformToggleBounds))
            {
                _isTogglePressed = true;
                Invalidate(_waveformBounds);
                return; // Consume the click — do NOT fall through to seek
            }

            // Check waveform hit
            if (WaveformRenderer.HitTest(e.Location, _waveformBounds))
            {
                if (_player != null)
                {
                    double duration = _player.TotalDurationSeconds;
                    if (duration > 0)
                    {
                        double ratio = GetWaveformTimeRatio(e.Location);
                        double seekTime = ratio * duration;

                        // Instant seek on click (per user decision)
                        _player.Seek(seekTime);

                        // Click while stopped starts playback (per user decision)
                        if (_player.State == AudioPlayerState.Stopped || _player.State == AudioPlayerState.Idle)
                        {
                            _player.Play();
                        }

                        _isWaveformDragging = true;
                        _waveformDragPosition = seekTime;
                        Invalidate(_waveformBounds);
                        Invalidate(controlBarBounds);
                        return;
                    }
                }
                // No player or zero duration — still mark dragging to consume events
                _isWaveformDragging = true;
                return;
            }

            HitZone zone = ControlBarRenderer.HitTest(e.Location, _controlBarLayout, dpiScale);
            _pressedZone = zone;

            // Autoplay/Loop checkboxes don't require a player — handle before null guard
            if (zone == HitZone.AutoplayCheckbox)
            {
                ToggleAutoplay();
                Invalidate(controlBarBounds);
                return;
            }
            if (zone == HitZone.LoopCheckbox)
            {
                ToggleLoop();
                Invalidate(controlBarBounds);
                return;
            }

            if (_player == null)
            {
                Invalidate(controlBarBounds);
                return;
            }

            switch (zone)
            {
                case HitZone.PlayPauseButton:
                    if (_player.State == AudioPlayerState.Playing)
                        _player.Pause();
                    else
                        _player.Play();
                    break;

                case HitZone.StopButton:
                    _player.Stop();
                    break;

                case HitZone.SeekBar:
                    if (_player.TotalDurationSeconds > 0)
                    {
                        double ratio = GetSeekRatio(e.Location, controlBarBounds, dpiScale);
                        _player.Seek(ratio * _player.TotalDurationSeconds);
                        _isSeeking = true;
                    }
                    break;

                case HitZone.VolumeIcon:
                    _isMuted = !_isMuted;
                    _player.SetMute(_isMuted);
                    SaveVolumeConfig();
                    break;

                case HitZone.VolumeSlider:
                    SetVolumeFromPoint(e.Location, controlBarBounds, dpiScale);
                    break;
            }

            Invalidate(controlBarBounds);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            float dpiScale = _dpiScale;
            int controlBarHeight = ControlBarRenderer.GetControlBarHeight(dpiScale);
            Rectangle controlBarBounds = new Rectangle(
                ClientRectangle.X,
                ClientRectangle.Bottom - controlBarHeight,
                ClientRectangle.Width,
                controlBarHeight);

            // Handle toggle button release
            if (_isTogglePressed)
            {
                _isTogglePressed = false;

                // Check if mouse is still over toggle button (actual click vs drag-away)
                if (WaveformRenderer.HitTestToggle(e.Location, _waveformToggleBounds))
                {
                    // Toggle color mode
                    _isWaveformColorMode = !_isWaveformColorMode;
                    SaveConfig(cfg => cfg.WaveformColorMode = _isWaveformColorMode);
                }

                Invalidate(_waveformBounds);
                return;
            }

            // Handle waveform drag release
            if (_isWaveformDragging)
            {
                // Seek to final drag position on release (per user decision)
                if (_player != null && _player.TotalDurationSeconds > 0)
                {
                    _player.Seek(_waveformDragPosition);
                }
                _isWaveformDragging = false;
                Invalidate(_waveformBounds);
                Invalidate(controlBarBounds);
                return;
            }

            if (_pressedZone == HitZone.VolumeSlider)
            {
                FlushPendingVolumeSave();
            }

            _pressedZone = HitZone.None;
            _isSeeking = false;
            Invalidate(controlBarBounds);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);

            // Clear owner-drawn tooltip on mouse leave
            _tooltipTimer?.Stop();
            if (_tooltipVisible || _tooltipText != null)
            {
                _tooltipVisible = false;
                _tooltipText = null;
                Invalidate();
            }

            // Clear gear icon hover state
            if (_isGearHovered)
            {
                _isGearHovered = false;
                Invalidate(_gearIconRect);
            }

            // Clear re-analyze hover state
            if (_isReanalyzeHovered)
            {
                _isReanalyzeHovered = false;
                Invalidate(_metadataBounds);
            }

            // Clear waveform hover/drag state and toggle state
            if (_isHoveringWaveform || _isWaveformDragging || _isToggleHovered || _isTogglePressed)
            {
                _isHoveringWaveform = false;
                _isWaveformDragging = false;
                _isToggleHovered = false;
                _isTogglePressed = false;
                Invalidate(_waveformBounds);
            }

            if (_hoveredZone != HitZone.None)
            {
                _hoveredZone = HitZone.None;
                Cursor = Cursors.Default;

                float dpiScale = _dpiScale;
                int controlBarHeight = ControlBarRenderer.GetControlBarHeight(dpiScale);
                Invalidate(new Rectangle(ClientRectangle.X, ClientRectangle.Bottom - controlBarHeight,
                    ClientRectangle.Width, controlBarHeight));
            }
        }

        // -------------------------------------------------------------------------
        // Keyboard shortcut action methods (called from AudioPreviewHandler.TranslateAccelerator)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Toggles between play and pause. If playing, pauses; if paused or stopped, plays.
        /// </summary>
        public void TogglePlayPause()
        {
            if (_player == null) return;
            if (_player.State == AudioPlayerState.Playing)
            {
                _player.Pause();
                StopPositionTimer();
            }
            else
            {
                _player.Play();
                StartPositionTimer();
            }
            Invalidate();
        }

        /// <summary>
        /// Seeks the playback position by an adaptive amount based on file duration.
        /// direction should be +1.0 (forward) or -1.0 (backward).
        /// Seek amount: 5% of duration, clamped to [0.5s, 15s].
        /// Clamps result to [0, duration]. Does nothing if no player or zero duration.
        /// </summary>
        public void SeekRelative(double direction)
        {
            if (_player == null) return;
            double duration = _player.TotalDurationSeconds;
            if (duration <= 0) return;
            double seekAmount = Math.Max(0.5, Math.Min(15.0, duration * 0.05)) * direction;
            double newPos = _player.CurrentPositionSeconds + seekAmount;
            newPos = Math.Max(0, Math.Min(duration, newPos));
            _player.Seek(newPos);
            Invalidate(_waveformBounds);
        }

        /// <summary>
        /// Adjusts the volume by delta (positive = louder, negative = quieter).
        /// Clamps to [0, 1]. If currently muted, unmutes before adjusting.
        /// Persists to config and invalidates control bar.
        /// </summary>
        public void AdjustVolume(float delta)
        {
            if (_player == null) return;
            // If muted, unmute first
            if (_isMuted)
            {
                _isMuted = false;
                _player.SetMute(false);
            }
            _volume = Math.Max(0f, Math.Min(1f, _volume + delta));
            _player.SetVolume(_volume);
            SaveVolumeConfig();
            float dpiScale = _dpiScale;
            int controlBarHeight = ControlBarRenderer.GetControlBarHeight(dpiScale);
            Invalidate(new Rectangle(ClientRectangle.X, ClientRectangle.Bottom - controlBarHeight,
                ClientRectangle.Width, controlBarHeight));
        }

        /// <summary>
        /// Toggles mute state. Persists to config and invalidates control bar.
        /// </summary>
        public void ToggleMute()
        {
            if (_player == null) return;
            _isMuted = !_isMuted;
            _player.SetMute(_isMuted);
            _player.SetVolume(_isMuted ? 0f : _volume);
            SaveVolumeConfig();
            float dpiScale = _dpiScale;
            int controlBarHeight = ControlBarRenderer.GetControlBarHeight(dpiScale);
            Invalidate(new Rectangle(ClientRectangle.X, ClientRectangle.Bottom - controlBarHeight,
                ClientRectangle.Width, controlBarHeight));
        }

        // -------------------------------------------------------------------------
        // Autoplay / loop public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Gets whether autoplay is currently enabled.
        /// </summary>
        public bool IsAutoplay => _isAutoplay;

        /// <summary>
        /// Gets whether loop is currently enabled.
        /// </summary>
        public bool IsLoop => _isLoop;

        /// <summary>
        /// Toggles autoplay state and persists to config.
        /// Called from checkbox click or keyboard shortcut.
        /// </summary>
        public void ToggleAutoplay()
        {
            _isAutoplay = !_isAutoplay;
            SavePlaybackConfig();
        }

        /// <summary>
        /// Toggles loop state and persists to config.
        /// Called from checkbox click or keyboard shortcut.
        /// </summary>
        public void ToggleLoop()
        {
            _isLoop = !_isLoop;
            SavePlaybackConfig();
        }

        /// <summary>
        /// Starts playback if the player is in Stopped, Paused, or Idle state.
        /// Called by autoplay timer in AudioPreviewHandler.
        /// </summary>
        public void Play()
        {
            if (_player == null) return;
            var state = _player.State;
            if (state == AudioPlayerState.Stopped || state == AudioPlayerState.Paused || state == AudioPlayerState.Idle)
            {
                _player.Play();
                StartPositionTimer();
                Invalidate();
            }
        }

        private void SavePlaybackConfig()
        {
            SaveConfig(cfg =>
            {
                cfg.Autoplay = _isAutoplay;
                cfg.Loop = _isLoop;
            });
        }

        // -------------------------------------------------------------------------
        // Volume helpers
        // -------------------------------------------------------------------------

        private void SetVolumeFromPoint(Point point, Rectangle controlBarBounds, float dpiScale)
        {
            if (_player == null) return;

            float ratio = GetVolumeRatioFromPoint(point, controlBarBounds, dpiScale);
            ratio = Math.Max(0f, Math.Min(1f, ratio));
            _volume = ratio;
            _isMuted = false; // Adjusting volume implicitly unmutes
            _player.SetVolume(_volume);
            _player.SetMute(false);
            ScheduleVolumeSave();
        }

        /// <summary>
        /// Approximates volume ratio from the x position within the estimated volume slider area.
        /// The volume slider is anchored to the right of the control bar.
        /// </summary>
        private float GetVolumeRatioFromPoint(Point point, Rectangle controlBarBounds, float dpiScale)
        {
            // Mirror the layout logic from ControlBarRenderer to find slider bounds
            int pad = (int)(8 * dpiScale);
            int buttonSize = (int)(24 * dpiScale);
            int volSliderWidth = (int)(80 * dpiScale);
            int volIconSize = (int)(24 * dpiScale);

            int volAreaWidth = volIconSize + pad / 2 + volSliderWidth;
            int volAreaLeft = controlBarBounds.Right - pad - volAreaWidth;
            int volSliderLeft = volAreaLeft + volIconSize + pad / 2;

            if (volSliderWidth <= 0) return _volume;

            float ratio = (float)(point.X - volSliderLeft) / volSliderWidth;
            return ratio;
        }

        private void SaveVolumeConfig()
        {
            _volumeSaveTimer?.Stop();
            _hasPendingVolumeSave = false;
            SaveConfig(cfg =>
            {
                cfg.Volume = _volume;
                cfg.IsMuted = _isMuted;
            });
        }

        private void ScheduleVolumeSave()
        {
            _hasPendingVolumeSave = true;
            _volumeSaveTimer?.Stop();
            _volumeSaveTimer?.Start();
        }

        private void FlushPendingVolumeSave()
        {
            if (!_hasPendingVolumeSave) return;
            _volumeSaveTimer?.Stop();
            _hasPendingVolumeSave = false;
            SaveVolumeConfig();
        }

        private void SaveConfig(Action<AppConfig> update)
        {
            try
            {
                update(_config);
                ConfigManager.Save(_config);
            }
            catch { }
        }

        private static string NormalizeKeyProfile(string? profile)
        {
            if (string.IsNullOrWhiteSpace(profile))
                return "auto";

            string normalized = profile!.Trim().ToLowerInvariant();
            if (normalized == "krumhansl" || normalized == "temperley" || normalized == "auto")
                return normalized;

            return "auto";
        }

        private static string BuildAnalysisCacheKey(byte[] audioData, string normalizedKeyProfile)
        {
            string baseHash = WaveformCache.ComputeCacheKey(audioData);
            return $"{baseHash}_kp_{normalizedKeyProfile}";
        }

        // -------------------------------------------------------------------------
        // Seek bar ratio helper
        // -------------------------------------------------------------------------

        private double GetSeekRatio(Point point, Rectangle controlBarBounds, float dpiScale)
        {
            // Mirror the seek bar layout logic from ControlBarRenderer
            int pad = (int)(8 * dpiScale);
            int timeLabelWidth = (int)(36 * dpiScale);
            int seekLeft = controlBarBounds.Left + pad + timeLabelWidth + pad;
            int seekRight = controlBarBounds.Right - pad - timeLabelWidth - pad;
            int seekWidth = seekRight - seekLeft;

            if (seekWidth <= 0) return 0;

            double ratio = (double)(point.X - seekLeft) / seekWidth;
            return Math.Max(0, Math.Min(1, ratio));
        }

        // -------------------------------------------------------------------------
        // Waveform X-to-time ratio helper
        // -------------------------------------------------------------------------

        /// <summary>
        /// Converts a mouse X position within the waveform area to a 0..1 time ratio.
        /// </summary>
        private double GetWaveformTimeRatio(Point point)
        {
            float dpiScale = _dpiScale;
            int padding = (int)(8 * dpiScale);
            int waveformLeft = _waveformBounds.Left + padding;
            int waveformRight = _waveformBounds.Right - padding;
            int waveformWidth = waveformRight - waveformLeft;
            if (waveformWidth <= 0) return 0;
            double ratio = (double)(point.X - waveformLeft) / waveformWidth;
            return Math.Max(0, Math.Min(1, ratio));
        }

        // -------------------------------------------------------------------------
        // Keyboard handling
        // -------------------------------------------------------------------------

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape && _settingsOpen)
            {
                CloseSettings();
                e.Handled = true;
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Escape && _settingsOpen)
                return true;
            return base.IsInputKey(keyData);
        }

        // -------------------------------------------------------------------------
        // Size changed — update cached overlay bounds
        // -------------------------------------------------------------------------

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);

            // Update overlay bounds immediately so click-outside detection stays accurate
            if (_settingsOpen)
            {
                _settingsOverlayBounds = SettingsOverlayRenderer.GetOverlayBounds(ClientRectangle, _dpiScale);
                _settingsOverlayLayout = SettingsOverlayLayout.Empty;
                _settingsOverlayLayout.OverlayBounds = _settingsOverlayBounds;
            }
        }

        // -------------------------------------------------------------------------
        // Dispose
        // -------------------------------------------------------------------------

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelBpmKeyAnalysis();
                CancelWaveformGeneration();
                StopLoading();
                StopPositionTimer();
                _positionTimer?.Dispose();
                _positionTimer = null;
                _tooltipTimer?.Stop();
                _tooltipTimer?.Dispose();
                _tooltipTimer = null;
                FlushPendingVolumeSave();
                _volumeSaveTimer?.Stop();
                _volumeSaveTimer?.Dispose();
                _volumeSaveTimer = null;

                if (_player != null)
                {
                    _player.StateChanged -= OnPlayerStateChanged;
                    _player.PlaybackEnded -= OnPlaybackEnded;
                    _player = null;
                }
            }
            base.Dispose(disposing);
        }

        // -------------------------------------------------------------------------
        // Native methods
        // -------------------------------------------------------------------------

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        }
    }
}
