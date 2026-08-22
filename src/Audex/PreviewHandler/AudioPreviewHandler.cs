using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Windows.Forms;
using Audex.Audio;
using Audex.Config;
using Audex.FileReader;
using Audex.Interop;
using Audex.UI;
using Audex.Utils;

namespace Audex.PreviewHandler
{
    /// <summary>
    /// Main COM-visible preview handler class implementing IPreviewHandler and related interfaces.
    /// Uses WinForms UserControl pattern: control is created on the STA thread in the constructor,
    /// then reparented under Explorer's preview pane HWND via SetParent.
    /// All UI work is marshaled to the STA thread via Control.Invoke.
    ///
    /// AudioPlayer is initialized once (BASS stays alive across file switches) and LoadFile is called
    /// per file. Unload frees only the current stream — not the BASS device.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    [Guid(ComGuids.AudioPreviewHandler)]
    [ProgId("Audex.AudioPreviewHandler")]
    public class AudioPreviewHandler : IPreviewHandler, IInitializeWithStream, IObjectWithSite, IOleWindow, IDisposable
    {
        // IInitializeWithStream state
        private IStream? _stream;
        private string _fileName = string.Empty;
        private long _fileSize;

        // IPreviewHandler state
        private IntPtr _hwndParent;
        private Rectangle _windowBounds;
        private bool _showPreview;

        // IObjectWithSite state
        private object? _site;
        private IPreviewHandlerFrame? _frame;

        // Preview state
        private AudioFileInfo? _audioFileInfo;
        private System.Threading.Timer? _debounceTimer;
        private System.Threading.Timer? _autoplayTimer;
        private bool _isFirstLoad = true;
        private readonly object _previewLifecycleLock = new object();
        private int _previewRequestId;

        // UI state — created on STA thread in constructor
        private PreviewWindow _previewWindow = null!;

        // Audio engine — initialized once in constructor, kept alive across file switches
        private AudioPlayer _player = null!;

        // Current file data — copied from IStream, held until Unload
        private byte[]? _fileData;

        private bool _disposed;

        // Constants
        private const int S_OK = 0;
        private const int S_FALSE = 1;
        private const int E_NOTIMPL = unchecked((int)0x80004001);
        private const int E_NOINTERFACE = unchecked((int)0x80004002);
        private const int MaxPreviewFileBytes = 256 * 1024 * 1024;

        // Keyboard message constants
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_SYSKEYDOWN = 0x0104;
        private const int VK_CONTROL = 0x11;

        /// <summary>
        /// Constructor runs on STA thread. Creates the WinForms control and AudioPlayer.
        /// Forces HWND creation so Control.Invoke works for later MTA calls.
        /// BASS is initialized here and kept alive until the COM object is destroyed.
        /// </summary>
        public AudioPreviewHandler()
        {
            try
            {
                Logger.Initialize();

                // Create the WinForms control on this STA thread
                _previewWindow = new PreviewWindow();
                // Force HWND creation NOW — this must happen on the STA thread
                IntPtr forceHandle = _previewWindow.Handle;

                // Initialize AudioPlayer (BASS+WASAPI) — kept alive across file switches
                _player = new AudioPlayer();
                var initConfig = ConfigManager.Load();
                bool bassOk = _player.Initialize(initConfig.WasapiDeviceIndex);

                if (!bassOk)
                {
                    Logger.Error("[AudioPreviewHandler] BASS initialization failed — audio playback unavailable");
                    // Don't crash the constructor — show error in UI when DoPreview is called
                }

                // Apply persisted volume/mute settings
                var config = initConfig;
                _player.SetVolume(config.IsMuted ? 0f : config.Volume);
                _player.SetMute(config.IsMuted);

                // Wire AudioPlayer into PreviewWindow
                _previewWindow.SetPlayer(_player);
            }
            catch (Exception ex)
            {
                Logger.Error($"[AudioPreviewHandler] Constructor exception: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Marshals an action to the STA thread that owns the preview control.
        /// </summary>
        private void InvokeOnUI(Action action)
        {
            if (_previewWindow != null && _previewWindow.IsHandleCreated)
            {
                try
                {
                    _previewWindow.Invoke(action);
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
        }

        /// <summary>
        /// Returns true when this callback/request is still the latest requested preview.
        /// </summary>
        private bool IsCurrentPreviewRequest(int requestId)
        {
            return requestId == Volatile.Read(ref _previewRequestId);
        }

        #region IInitializeWithStream Implementation

        public void Initialize(IStream pstream, uint grfMode)
        {
            try
            {
                lock (_previewLifecycleLock)
                {
                    // Invalidate any in-flight preview callbacks bound to the previous stream.
                    Interlocked.Increment(ref _previewRequestId);

                    // Some hosts may call Initialize again without an intervening Unload — release
                    // the previous stream's COM reference before overwriting it, or it leaks.
                    if (_stream != null && !ReferenceEquals(_stream, pstream))
                    {
                        try { Marshal.ReleaseComObject(_stream); } catch { }
                    }
                    _stream = pstream;

                    try
                    {
                        System.Runtime.InteropServices.ComTypes.STATSTG stat;

                        // Try with name first (flag=0), fall back to STATFLAG_NONAME (flag=1)
                        try
                        {
                            pstream.Stat(out stat, 0);
                            _fileSize = stat.cbSize;
                            _fileName = stat.pwcsName ?? "Unknown";
                        }
                        catch
                        {
                            pstream.Stat(out stat, 1); // STATFLAG_NONAME
                            _fileSize = stat.cbSize;
                            _fileName = "Unknown";
                        }
                    }
                    catch (Exception statEx)
                    {
                        Logger.Error($"Stat failed: {statEx.Message}", statEx);
                        _fileSize = 0;
                        _fileName = "Unknown";
                    }
                }

                Logger.Info($"Stream initialized: {_fileName}, {_fileSize} bytes");
            }
            catch (Exception ex)
            {
                Logger.Error($"Initialize failed: {ex.Message}", ex);
            }
        }

        #endregion

        #region IPreviewHandler Implementation

        public void SetWindow(IntPtr hwnd, ref RECT rect)
        {
            try
            {
                _hwndParent = hwnd;
                _windowBounds = new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);

                UpdateWindowBounds();
            }
            catch (Exception ex)
            {
                Logger.Error($"SetWindow failed: {ex.Message}", ex);
            }
        }

        public void SetRect(ref RECT rect)
        {
            try
            {
                _windowBounds = new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height);

                UpdateWindowBounds();
            }
            catch (Exception ex)
            {
                Logger.Error($"SetRect failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Reparents the WinForms control under Explorer's HWND and sizes it.
        /// All work is marshaled to the STA thread via Control.Invoke.
        /// </summary>
        private void UpdateWindowBounds()
        {
            if (!_showPreview) return;
            if (_hwndParent == IntPtr.Zero) return;
            if (_windowBounds.Width <= 0 || _windowBounds.Height <= 0) return;

            try
            {
                InvokeOnUI(() =>
                {
                    NativeMethods.SetParent(_previewWindow.Handle, _hwndParent);
                    _previewWindow.Bounds = _windowBounds;
                    _previewWindow.Visible = true;
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"UpdateWindowBounds failed: {ex.Message}", ex);
            }
        }

        public void DoPreview()
        {
            try
            {
                _showPreview = true;
                int requestId = Interlocked.Increment(ref _previewRequestId);
                bool runImmediate;
                int debounceMs = 0;

                lock (_previewLifecycleLock)
                {
                    _debounceTimer?.Dispose();
                    _debounceTimer = null;

                    if (_isFirstLoad)
                    {
                        _isFirstLoad = false;
                        runImmediate = true;
                    }
                    else
                    {
                        runImmediate = false;
                        debounceMs = Math.Max(0, ConfigManager.Load().DebounceMs);
                        // Marshal onto the STA thread before touching _stream/_player — System.Threading.Timer
                        // callbacks run on a raw ThreadPool thread, which never called CoInitializeEx and may
                        // not share an apartment with the shell-provided IStream.
                        _debounceTimer = new System.Threading.Timer(_ => InvokeOnUI(() => DoPreviewInternal(requestId)), null, debounceMs, Timeout.Infinite);
                    }
                }

                if (runImmediate)
                {
                    DoPreviewInternal(requestId);
                }
                else
                {
                    Logger.Debug($"Debounced DoPreview (delay: {debounceMs}ms)");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"DoPreview failed: {ex.Message}", ex);
            }
        }

        private void DoPreviewInternal(int requestId)
        {
            if (!IsCurrentPreviewRequest(requestId))
                return;

            try
            {
                lock (_previewLifecycleLock)
                {
                    if (!IsCurrentPreviewRequest(requestId) || !_showPreview)
                        return;

                    // Cancel any pending autoplay from the previous file
                    _autoplayTimer?.Dispose();
                    _autoplayTimer = null;

                    if (_stream == null)
                        return;

                    // If BASS failed to initialize, show error immediately
                    if (_player.State == AudioPlayerState.Error)
                    {
                        InvokeOnUI(() =>
                        {
                            if (!IsCurrentPreviewRequest(requestId)) return;
                            _previewWindow.StopLoading();
                            _previewWindow.ShowError("Audio engine failed to initialize. Check that bass.dll is present and this system has a WASAPI-compatible audio device.");
                        });
                        UpdateWindowBounds();
                        return;
                    }

                    // Start loading spinner
                    InvokeOnUI(() =>
                    {
                        if (!IsCurrentPreviewRequest(requestId)) return;
                        _previewWindow.StartLoading();
                    });

                    // Copy IStream to byte array
                    _fileData = CopyStreamToBytes(_stream, requestId);
                    if (!IsCurrentPreviewRequest(requestId) || !_showPreview)
                        return;

                    // Parse file header for bit depth (header parsers know actual bit depth;
                    // BASS reports 32-bit float for decode streams). Parse from the bytes we just
                    // buffered rather than re-reading the shell's IStream a second time.
                    IStream headerSource = _fileData is { Length: > 0 }
                        ? new InMemoryComStream(_fileData)
                        : _stream;
                    AudioFileInfo headerInfo = AudioHeaderParserFactory.Parse(headerSource, _fileName, _fileSize);

                    // Determine module format and check format support via PluginManager
                    string fileExt = Path.GetExtension(_fileName)?.ToLowerInvariant() ?? "";
                    bool isModule = PluginManager.IsModuleFormat(fileExt);
                    bool formatSupported = PluginManager.IsFormatSupported(fileExt);
                    string? unsupportedFormatReason = formatSupported ? null : PluginManager.GetUnsupportedReason(fileExt);

                    // Check if WASAPI device needs (re-)switching: either the user changed it in
                    // settings, or a previous switch/init failed (e.g. device was disconnected) and
                    // we should retry now rather than staying broken for the rest of the session.
                    int configDeviceIndex = ConfigManager.Load().WasapiDeviceIndex;
                    if (configDeviceIndex != _player.CurrentDeviceIndex || !_player.IsWasapiReady)
                    {
                        bool switched = _player.SwitchDevice(configDeviceIndex);
                        if (switched)
                        {
                            // Reapply volume/mute after device switch (WASAPI session volume is per-device)
                            var volConfig = ConfigManager.Load();
                            _player.SetVolume(volConfig.IsMuted ? 0f : volConfig.Volume);
                            _player.SetMute(volConfig.IsMuted);
                        }
                    }

                    string? formatError = ResolvePreLoadError(formatSupported, unsupportedFormatReason, _player.IsWasapiReady);

                    // Load file into AudioPlayer — get BASS-derived sample rate, channels, duration
                    int sampleRate = headerInfo.SampleRate;
                    int channels = headerInfo.Channels;
                    double duration = headerInfo.Duration;

                    if (_fileData != null && formatError == null)
                    {
                        try
                        {
                            var (bassSampleRate, bassChannels, bassDuration) = _player.LoadFile(_fileData, _fileName);
                            // Use BASS values for sample rate, channels, duration (authoritative)
                            sampleRate = bassSampleRate;
                            channels = bassChannels;
                            duration = bassDuration;
                        }
                        catch (Exception playerEx)
                        {
                            formatError = $"Format Unavailable: {playerEx.Message}";
                            // Continue — still show metadata even when playback fails
                        }
                    }

                    if (!IsCurrentPreviewRequest(requestId) || !_showPreview)
                        return;

                    // Read tags
                    TagInfo tags = _fileData != null
                        ? TagReader.ReadTags(_fileData, _fileName)
                        : new TagInfo(null, null, null);

                    // Read BPM and key from all available tag types (ID3v2, Vorbis, APE, Serato GEOB)
                    MusicInfo musicInfo = _fileData != null
                        ? TagReader.ReadMusicInfo(_fileData, _fileName)
                        : new MusicInfo(null, null);

                    // Build unified AudioFileInfo
                    _audioFileInfo = new AudioFileInfo
                    {
                        FileName = headerInfo.FileName,
                        FileSize = headerInfo.FileSize,
                        Format = headerInfo.Format,
                        SampleRate = sampleRate,
                        BitDepth = headerInfo.BitDepth, // From header parser (actual depth, not BASS float)
                        Channels = channels,
                        Duration = duration,
                        BitRate = headerInfo.BitRate,
                        ParseSucceeded = headerInfo.ParseSucceeded,
                        ParseError = headerInfo.ParseError,
                        Title = tags.Title,
                        Artist = tags.Artist,
                        Album = tags.Album,
                        Bpm = musicInfo.Bpm,
                        Key = musicInfo.Key,
                        IsModuleFormat = isModule,
                        FormatError = formatError
                    };

                    if (!IsCurrentPreviewRequest(requestId) || !_showPreview)
                        return;

                    // Update UI on STA thread
                    InvokeOnUI(() =>
                    {
                        if (!IsCurrentPreviewRequest(requestId)) return;
                        _previewWindow.StopLoading();
                        _previewWindow.UpdateContent(_audioFileInfo);
                        // Header parsers are best-effort only. If decode/playback succeeded (formatError == null),
                        // suppress parser errors so users don't see false "can't preview" banners.
                        if (!_audioFileInfo.ParseSucceeded && _audioFileInfo.FormatError != null)
                        {
                            _previewWindow.ShowError(_audioFileInfo.ParseError ?? "Unknown error");
                        }
                    });

                    // Start waveform generation (background thread, uses separate BASS decode stream)
                    // Skip waveform generation when format cannot be decoded
                    if (_fileData != null && formatError == null)
                    {
                        byte[] fileDataRef = _fileData; // capture reference for background thread
                        double waveformDuration = duration;
                        InvokeOnUI(() =>
                        {
                            if (!IsCurrentPreviewRequest(requestId)) return;
                            _previewWindow.StartWaveformGeneration(fileDataRef, waveformDuration, isModule);
                        });
                    }

                    // Start BPM/key analysis (background thread, separate BASS decode stream)
                    // Only for non-module formats without existing tags, and when format is decodable
                    if (_fileData != null && formatError == null)
                    {
                        byte[] analysisDataRef = _fileData;
                        bool hasBpmTag = musicInfo.Bpm.HasValue;
                        bool hasKeyTag = !string.IsNullOrEmpty(musicInfo.Key);
                        double analysisDuration = duration;
                        InvokeOnUI(() =>
                        {
                            if (!IsCurrentPreviewRequest(requestId)) return;
                            _previewWindow.StartBpmKeyAnalysis(
                                analysisDataRef, isModule, analysisDuration, hasBpmTag, hasKeyTag);
                        });
                    }

                    UpdateWindowBounds();

                    // Autoplay: if enabled, schedule playback after delay.
                    // Read live state from PreviewWindow (authoritative for current session).
                    bool isAutoplay = _previewWindow.IsAutoplay;
                    int delayMs = Math.Max(0, ConfigManager.Load().AutoplayDelayMs);
                    if (isAutoplay && formatError == null)
                    {
                        _autoplayTimer = new System.Threading.Timer(_ =>
                        {
                            try
                            {
                                if (!IsCurrentPreviewRequest(requestId))
                                    return;

                                lock (_previewLifecycleLock)
                                {
                                    if (!IsCurrentPreviewRequest(requestId) || !_showPreview)
                                        return;

                                    _autoplayTimer?.Dispose();
                                    _autoplayTimer = null;
                                }

                                InvokeOnUI(() =>
                                {
                                    if (!IsCurrentPreviewRequest(requestId)) return;
                                    _previewWindow.Play();
                                });
                            }
                            catch (Exception autoEx)
                            {
                                Logger.Error($"Autoplay timer callback exception: {autoEx.Message}", autoEx);
                            }
                        }, null, delayMs, System.Threading.Timeout.Infinite);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!IsCurrentPreviewRequest(requestId))
                    return;

                Logger.Error($"DoPreviewInternal failed: {ex.Message}", ex);

                _audioFileInfo = new AudioFileInfo
                {
                    FileName = _fileName,
                    FileSize = _fileSize,
                    ParseSucceeded = false,
                    ParseError = ex.Message
                };

                try
                {
                    InvokeOnUI(() =>
                    {
                        _previewWindow.StopLoading();
                        _previewWindow.UpdateContent(_audioFileInfo);
                        _previewWindow.ShowError(ex.Message);
                    });
                }
                catch { }
            }
        }

        /// <summary>
        /// Decides which error (if any) should block loading/playing the file, before LoadFile is
        /// attempted. Format-unsupported takes precedence over device-unavailable, since it's the
        /// more specific/actionable reason and remains true regardless of device state. Pulled out
        /// as a pure function so the precedence rule is unit-testable without a real AudioPlayer.
        /// </summary>
        internal static string? ResolvePreLoadError(bool formatSupported, string? unsupportedFormatReason, bool wasapiReady)
        {
            if (!formatSupported)
                return unsupportedFormatReason;

            if (!wasapiReady)
                return "Audio output device unavailable. Check your Windows sound settings.";

            return null;
        }

        /// <summary>
        /// Copies the entire IStream to a byte array using 64KB chunks.
        /// Uses the Marshal-based IntPtr pattern from StreamHelper (COM IStream.Read takes IntPtr for bytesRead).
        /// Runs on the STA thread (see DoPreview/InvokeOnUI), so for large/slow (e.g. network) files this
        /// would otherwise block the same message pump the loading spinner depends on. To keep the UI
        /// responsive, the message queue is pumped periodically. This is deliberately safe against the
        /// reentrancy that opens up: requestId is checked before continuing, and if a reentrant Unload()
        /// releases pstream mid-copy, the resulting COM exception is treated as a clean abandonment
        /// (the caller already discards results for a stale requestId) rather than an error.
        /// </summary>
        private byte[] CopyStreamToBytes(IStream pstream, int requestId)
        {
            try
            {
                return StreamHelper.ReadToEndBounded(
                    pstream,
                    _fileSize,
                    MaxPreviewFileBytes,
                    () => IsCurrentPreviewRequest(requestId),
                    Application.DoEvents);
            }
            catch when (!IsCurrentPreviewRequest(requestId))
            {
                // pstream was released by a reentrant Unload() while we were pumping messages above —
                // expected once the request is stale, not a real failure.
                return Array.Empty<byte>();
            }
        }

        public void Unload()
        {
            try
            {
                // Invalidate all in-flight/pending preview callbacks before teardown.
                Interlocked.Increment(ref _previewRequestId);

                lock (_previewLifecycleLock)
                {
                    _showPreview = false;

                    _debounceTimer?.Dispose();
                    _debounceTimer = null;

                    // Cancel any pending autoplay timer
                    _autoplayTimer?.Dispose();
                    _autoplayTimer = null;

                    // Cancel any in-progress BPM/key analysis
                    try
                    {
                        InvokeOnUI(() => _previewWindow.CancelBpmKeyAnalysis());
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"CancelBpmKeyAnalysis failed: {ex.Message}", ex);
                    }

                    // Cancel any in-progress waveform generation before stopping playback
                    try
                    {
                        InvokeOnUI(() => _previewWindow.CancelWaveformGeneration());
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"CancelWaveformGeneration failed: {ex.Message}", ex);
                    }

                    // Stop current playback and free the BASS decode stream (BASS device stays alive)
                    try
                    {
                        _player?.StopAndFreeCurrentStream();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"StopAndFreeCurrentStream failed: {ex.Message}", ex);
                    }

                    if (_stream != null)
                    {
                        Marshal.ReleaseComObject(_stream);
                        _stream = null;
                    }

                    // Allow GC to reclaim file data
                    _fileData = null;

                    // Hide and clear the control on STA thread
                    try
                    {
                        InvokeOnUI(() =>
                        {
                            _previewWindow.Visible = false;
                            _previewWindow.StopLoading();
                        });
                    }
                    catch { }

                    _audioFileInfo = null;
                    _isFirstLoad = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Unload failed: {ex.Message}", ex);
            }
        }

        public void SetFocus()
        {
            try
            {
                if (_previewWindow != null && _previewWindow.IsHandleCreated)
                {
                    InvokeOnUI(() => _previewWindow.Focus());
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SetFocus failed: {ex.Message}", ex);
            }
        }

        public void QueryFocus(out IntPtr phwnd)
        {
            try
            {
                phwnd = NativeMethods.GetFocus();
            }
            catch (Exception ex)
            {
                Logger.Error($"QueryFocus failed: {ex.Message}", ex);
                phwnd = IntPtr.Zero;
            }
        }

        public uint TranslateAccelerator(ref MSG pmsg)
        {
            try
            {
                // Only handle key-down messages
                if (pmsg.message == WM_KEYDOWN || pmsg.message == WM_SYSKEYDOWN)
                {
                    int vk = (int)pmsg.wParam;
                    bool ctrlDown = (NativeMethods.GetKeyState(VK_CONTROL) & 0x8000) != 0;

                    if (ctrlDown)
                    {
                        switch (vk)
                        {
                            case (int)Keys.Space:
                                InvokeOnUI(() => _previewWindow.TogglePlayPause());
                                return (uint)S_OK;

                            case (int)Keys.Left:
                                InvokeOnUI(() => _previewWindow.SeekRelative(-1.0));
                                return (uint)S_OK;

                            case (int)Keys.Right:
                                InvokeOnUI(() => _previewWindow.SeekRelative(1.0));
                                return (uint)S_OK;

                            case (int)Keys.Up:
                                InvokeOnUI(() => _previewWindow.AdjustVolume(0.05f));
                                return (uint)S_OK;

                            case (int)Keys.Down:
                                InvokeOnUI(() => _previewWindow.AdjustVolume(-0.05f));
                                return (uint)S_OK;

                            case (int)Keys.L:
                                InvokeOnUI(() => _previewWindow.ToggleLoop());
                                return (uint)S_OK;

                            case (int)Keys.M:
                                InvokeOnUI(() => _previewWindow.ToggleMute());
                                return (uint)S_OK;

                            case 0xBC: // Keys.OemComma
                                InvokeOnUI(() => _previewWindow.ToggleSettings());
                                return (uint)S_OK;
                        }
                    }
                    else if (vk == (int)Keys.Escape)
                    {
                        if (_previewWindow.IsSettingsOpen)
                        {
                            InvokeOnUI(() => _previewWindow.CloseSettings());
                            return (uint)S_OK;
                        }
                    }
                }

                // Forward unhandled keys to Explorer's frame
                if (_frame != null)
                {
                    return _frame.TranslateAccelerator(ref pmsg);
                }
                return (uint)S_FALSE;
            }
            catch (Exception ex)
            {
                Logger.Error($"TranslateAccelerator failed: {ex.Message}", ex);
                return (uint)S_FALSE;
            }
        }

        #endregion

        #region IObjectWithSite Implementation

        public void SetSite(object pUnkSite)
        {
            try
            {
                _site = pUnkSite;
                if (_site != null)
                {
                    try { _frame = _site as IPreviewHandlerFrame; }
                    catch { _frame = null; }
                }
                else
                {
                    _frame = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"SetSite failed: {ex.Message}", ex);
            }
        }

        public void GetSite(ref Guid riid, out object ppvSite)
        {
            if (_site != null)
            {
                ppvSite = _site;
            }
            else
            {
                ppvSite = null!;
                Marshal.ThrowExceptionForHR(E_NOINTERFACE);
            }
        }

        #endregion

        #region IOleWindow Implementation

        public void GetWindow(out IntPtr phwnd)
        {
            try
            {
                phwnd = _previewWindow?.Handle ?? _hwndParent;
            }
            catch (Exception ex)
            {
                Logger.Error($"GetWindow failed: {ex.Message}", ex);
                phwnd = IntPtr.Zero;
            }
        }

        public void ContextSensitiveHelp(bool fEnterMode)
        {
            Marshal.ThrowExceptionForHR(E_NOTIMPL);
        }

        #endregion

        #region IDisposable / Finalizer

        /// <summary>
        /// Deterministically tears down native resources. There is no shell-provided "goodbye"
        /// hook for a COM preview handler (releasing the last CCW reference does not call
        /// Dispose), so this exists mainly so an explicit caller (or future host) can trigger
        /// prompt cleanup instead of waiting on GC finalization. Safe to call multiple times.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                // These touch managed/COM objects and WinForms controls, so they must only run
                // when we know we're not on the finalizer thread (no COM apartment, wrong thread
                // for the control's HWND).
                try { _debounceTimer?.Dispose(); } catch { }
                try { _autoplayTimer?.Dispose(); } catch { }

                if (_stream != null)
                {
                    try { Marshal.ReleaseComObject(_stream); } catch { }
                    _stream = null;
                }

                try
                {
                    if (_previewWindow != null && _previewWindow.IsHandleCreated)
                        _previewWindow.Invoke(new Action(() => _previewWindow.Dispose()));
                    else
                        _previewWindow?.Dispose();
                }
                catch { }
            }

            // Native BASS/WASAPI teardown: BASS is documented as callable from any thread, so
            // this is safe to run unconditionally, including from the finalizer thread — it is
            // the only cleanup that path can safely perform.
            try { _player?.Shutdown(); } catch { }
        }

        ~AudioPreviewHandler()
        {
            Dispose(false);
        }

        #endregion

        #region Native Methods

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

            [DllImport("user32.dll")]
            public static extern IntPtr GetFocus();

            [DllImport("user32.dll")]
            public static extern short GetKeyState(int nVirtKey);
        }

        #endregion
    }
}
