using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using ManagedBass;
using ManagedBass.Mix;
using ManagedBass.Wasapi;
using Audex.Utils;

namespace Audex.Audio
{
    /// <summary>
    /// Audio playback engine using BASS with WASAPI shared-mode output and BassMix for
    /// sample rate conversion. Initialized once per COM object lifetime; individual files
    /// are loaded/unloaded via <see cref="LoadFile"/> and <see cref="StopAndFreeCurrentStream"/>.
    /// </summary>
    public sealed class AudioPlayer : IDisposable
    {
        [DllImport("kernel32.dll", EntryPoint = "RtlZeroMemory")]
        private static extern void ZeroMemory(IntPtr dest, int size);

        // --- Fields ---

        private int _decodeStream;
        private int _mixerStream;
        private GCHandle? _pinnedBuffer;
        private bool _isModuleHandle;
        private AudioPlayerState _state = AudioPlayerState.Idle;
        private WasapiProcedure? _wasapiProc;
        private int _endOfStreamFlag;
        private bool _isInitialized;
        private double _totalDurationSeconds;
        private bool _disposed;
        private int _wasapiFreq;
        private int _wasapiChannels;
        private int _currentDeviceIndex = -1;

        // --- Events ---

        /// <summary>Raised when the playback state changes.</summary>
        public event EventHandler<AudioPlayerState>? StateChanged;

        /// <summary>Raised when the current track reaches the end.</summary>
        public event EventHandler? PlaybackEnded;

        // --- Properties ---

        /// <summary>Gets the current playback position in seconds.</summary>
        public double CurrentPositionSeconds
        {
            get
            {
                if (_decodeStream == 0) return 0;
                long posBytes = Bass.ChannelGetPosition(_decodeStream);
                if (posBytes < 0) return 0;
                return Bass.ChannelBytes2Seconds(_decodeStream, posBytes);
            }
        }

        /// <summary>Gets the total duration of the loaded file in seconds, or 0 if none is loaded.</summary>
        public double TotalDurationSeconds => _totalDurationSeconds;

        /// <summary>Gets the current playback state.</summary>
        public AudioPlayerState State => _state;

        /// <summary>Gets the WASAPI device index currently in use, or -1 for the default device.</summary>
        public int CurrentDeviceIndex => _currentDeviceIndex;

        // --- Initialization ---

        /// <summary>
        /// Initializes BASS (NoSound device), WASAPI shared-mode output, and the BassMix mixer.
        /// Call once per COM object lifetime. Returns false on failure.
        /// </summary>
        /// <param name="deviceIndex">WASAPI output device index, or -1 for default.</param>
        public bool Initialize(int deviceIndex = -1)
        {
            if (_isInitialized) return true;

            try
            {
                string? assemblyDir = Path.GetDirectoryName(typeof(AudioPlayer).Assembly.Location);
                if (string.IsNullOrWhiteSpace(assemblyDir) ||
                    !NativeBassLoader.LoadCoreLibraries(assemblyDir))
                {
                    Logger.Error("[AudioPlayer] Failed to load native BASS libraries from assembly directory");
                    SetState(AudioPlayerState.Error);
                    return false;
                }

                _wasapiProc = WasapiCallback;

                // Bass.Init(0) = NoSound device — required for WASAPI decode-stream path
                if (!Bass.Init(0))
                {
                    Logger.Error($"[AudioPlayer] Bass.Init(0) failed: {Bass.LastError}");
                    SetState(AudioPlayerState.Error);
                    return false;
                }

                // Load BASS plugins for extended format support (AAC, WMA, Opus)
                PluginManager.LoadPlugins(assemblyDir);

                // Initialize WASAPI in shared mode on requested output device
                _currentDeviceIndex = deviceIndex;
                if (!BassWasapi.Init(deviceIndex, 0, 0, WasapiInitFlags.Shared, 0f, 0f, _wasapiProc, IntPtr.Zero))
                {
                    Logger.Error($"[AudioPlayer] BassWasapi.Init failed: {Bass.LastError}");
                    Bass.Free();
                    SetState(AudioPlayerState.Error);
                    return false;
                }

                // Get the actual WASAPI device format for the mixer
                var wasapiInfo = BassWasapi.Info;
                _wasapiFreq = wasapiInfo.Frequency;
                _wasapiChannels = wasapiInfo.Channels;
                Logger.Info($"[AudioPlayer] WASAPI device index={deviceIndex}: {_wasapiFreq}Hz / {_wasapiChannels}ch");

                // Create a mixer stream at the WASAPI device's sample rate/channels.
                // BassMix handles sample rate conversion and channel mixing automatically.
                _mixerStream = BassMix.CreateMixerStream(
                    _wasapiFreq, _wasapiChannels,
                    BassFlags.Decode | BassFlags.Float);

                if (_mixerStream == 0)
                {
                    Logger.Error($"[AudioPlayer] CreateMixerStream failed: {Bass.LastError}");
                    BassWasapi.Free();
                    Bass.Free();
                    SetState(AudioPlayerState.Error);
                    return false;
                }

                if (!BassWasapi.Start())
                {
                    Logger.Error($"[AudioPlayer] BassWasapi.Start() failed: {Bass.LastError}");
                    Bass.StreamFree(_mixerStream);
                    _mixerStream = 0;
                    BassWasapi.Free();
                    Bass.Free();
                    SetState(AudioPlayerState.Error);
                    return false;
                }

                _isInitialized = true;
                Logger.Info("[AudioPlayer] Initialized — BASS NoSound + WASAPI shared + BassMix");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[AudioPlayer] Initialize exception: {ex.Message}", ex);
                SetState(AudioPlayerState.Error);
                return false;
            }
        }

        // --- Stream lifecycle ---

        /// <summary>
        /// Loads a tracked-music module file (MOD/XM/IT/S3M) via Bass.MusicLoad.
        /// MusicLoad copies data internally — no GCHandle pinning required.
        /// </summary>
        private (int SampleRate, int Channels, double DurationSeconds) LoadModuleFile(byte[] data, string fileName)
        {
            // byte[] overload: MusicLoad copies data internally, no pinning needed.
            // BassFlags.Prescan: enables accurate byte-length reporting for seeking.
            int stream = Bass.MusicLoad(data, 0, data.Length,
                BassFlags.Decode | BassFlags.Float | BassFlags.Prescan, 0);

            if (stream == 0)
            {
                Logger.Error($"[AudioPlayer] MusicLoad failed for '{fileName}': {Bass.LastError}");
                SetState(AudioPlayerState.Idle);
                throw new InvalidOperationException($"BASS MusicLoad failed: {Bass.LastError}");
            }

            _decodeStream = stream;
            _isModuleHandle = true;
            // No pinned buffer to track — MusicLoad owns its own copy
            _pinnedBuffer = null;

            Bass.ChannelGetInfo(stream, out ChannelInfo info);
            double duration = Bass.ChannelBytes2Seconds(stream, Bass.ChannelGetLength(stream));
            _totalDurationSeconds = duration > 0 ? duration : 0;

            if (!BassMix.MixerAddChannel(_mixerStream, _decodeStream,
                BassFlags.MixerChanDownMix | BassFlags.MixerChanPause))
            {
                Logger.Error($"[AudioPlayer] MixerAddChannel (module) failed: {Bass.LastError}");
                Bass.MusicFree(_decodeStream);
                _decodeStream = 0;
                _isModuleHandle = false;
                SetState(AudioPlayerState.Idle);
                throw new InvalidOperationException($"BassMix.MixerAddChannel failed: {Bass.LastError}");
            }

            Interlocked.Exchange(ref _endOfStreamFlag, 0);
            SetState(AudioPlayerState.Stopped);

            Logger.Info($"[AudioPlayer] Loaded module '{fileName}' — {info.Frequency}Hz/{info.Channels}ch/{duration:F1}s");
            return (info.Frequency, info.Channels, _totalDurationSeconds);
        }

        /// <summary>
        /// Loads an audio file from an in-memory byte array, creates a decode stream, and
        /// adds it to the mixer. Returns the stream's sample rate, channel count, and duration.
        /// </summary>
        /// <param name="data">Raw file bytes (pinned for the lifetime of the stream).</param>
        /// <param name="fileName">Original file name, used for format detection and logging.</param>
        public (int SampleRate, int Channels, double DurationSeconds) LoadFile(byte[] data, string fileName)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("AudioPlayer not initialized. Call Initialize() first.");

            // Stop and free any existing stream
            if (_state == AudioPlayerState.Playing || _state == AudioPlayerState.Paused
                || _state == AudioPlayerState.Stopped)
            {
                StopAndFreeStream();
            }

            SetState(AudioPlayerState.Loading);

            // Dispatch module formats to MusicLoad path
            bool isModule = PluginManager.IsModuleFormat(System.IO.Path.GetExtension(fileName));
            if (isModule)
                return LoadModuleFile(data, fileName);

            _isModuleHandle = false;
            _pinnedBuffer = GCHandle.Alloc(data, GCHandleType.Pinned);

            try
            {
                // BassFlags.Decode: no direct output (feeds mixer)
                // BassFlags.Float: mixer expects 32-bit float PCM
                int stream = Bass.CreateStream(
                    _pinnedBuffer.Value.AddrOfPinnedObject(),
                    0,
                    data.Length,
                    BassFlags.Decode | BassFlags.Float);

                if (stream == 0)
                {
                    _pinnedBuffer.Value.Free();
                    _pinnedBuffer = null;
                    Logger.Error($"[AudioPlayer] CreateStream failed for '{fileName}': {Bass.LastError}");
                    SetState(AudioPlayerState.Idle);
                    throw new InvalidOperationException($"BASS stream creation failed: {Bass.LastError}");
                }

                _decodeStream = stream;

                // Read technical metadata from BASS
                Bass.ChannelGetInfo(stream, out ChannelInfo info);
                double duration = Bass.ChannelBytes2Seconds(stream, Bass.ChannelGetLength(stream));
                _totalDurationSeconds = duration > 0 ? duration : 0;

                // Add the decode stream to the mixer — BassMix handles sample rate
                // conversion (e.g. 44100→48000) and channel mixing (mono→stereo) automatically.
                // DownMix flag ensures proper channel mapping when source differs from mixer.
                if (!BassMix.MixerAddChannel(_mixerStream, _decodeStream,
                    BassFlags.MixerChanDownMix | BassFlags.MixerChanPause))
                {
                    Logger.Error($"[AudioPlayer] MixerAddChannel failed: {Bass.LastError}");
                    Bass.StreamFree(_decodeStream);
                    _decodeStream = 0;
                    _pinnedBuffer.Value.Free();
                    _pinnedBuffer = null;
                    SetState(AudioPlayerState.Idle);
                    throw new InvalidOperationException($"BassMix.MixerAddChannel failed: {Bass.LastError}");
                }

                Interlocked.Exchange(ref _endOfStreamFlag, 0);
                SetState(AudioPlayerState.Stopped);

                Logger.Info($"[AudioPlayer] Loaded '{fileName}' — {info.Frequency}Hz/{info.Channels}ch/{duration:F1}s (mixer: {_wasapiFreq}Hz/{_wasapiChannels}ch)");
                return (info.Frequency, info.Channels, _totalDurationSeconds);
            }
            catch
            {
                if (_pinnedBuffer.HasValue)
                {
                    _pinnedBuffer.Value.Free();
                    _pinnedBuffer = null;
                }
                throw;
            }
        }

        // --- Transport controls ---

        /// <summary>Begins or resumes playback of the loaded stream.</summary>
        public void Play()
        {
            if (!_isInitialized || _decodeStream == 0) return;
            if (_state != AudioPlayerState.Stopped && _state != AudioPlayerState.Paused) return;

            if (_state == AudioPlayerState.Stopped)
            {
                long totalBytes = Bass.ChannelGetLength(_decodeStream);
                long currentPos = Bass.ChannelGetPosition(_decodeStream);
                if (totalBytes > 0 && currentPos >= totalBytes)
                {
                    Bass.ChannelSetPosition(_decodeStream, 0);
                    Interlocked.Exchange(ref _endOfStreamFlag, 0);
                }
            }

            // Unpause the channel in the mixer to start feeding data
            BassMix.ChannelFlags(_decodeStream, BassFlags.Default, BassFlags.MixerChanPause);
            SetState(AudioPlayerState.Playing);
        }

        /// <summary>Pauses playback at the current position.</summary>
        public void Pause()
        {
            if (_state == AudioPlayerState.Playing)
            {
                // Pause the channel in the mixer — stops feeding data, mixer outputs silence
                BassMix.ChannelFlags(_decodeStream, BassFlags.MixerChanPause, BassFlags.MixerChanPause);
                SetState(AudioPlayerState.Paused);
            }
        }

        /// <summary>Stops playback and resets the position to the beginning.</summary>
        public void Stop()
        {
            if (_decodeStream == 0) return;
            // Pause the channel in the mixer first, then reset position
            BassMix.ChannelFlags(_decodeStream, BassFlags.MixerChanPause, BassFlags.MixerChanPause);
            SetState(AudioPlayerState.Stopped);
            Bass.ChannelSetPosition(_decodeStream, 0);
            Interlocked.Exchange(ref _endOfStreamFlag, 0);
        }

        /// <summary>Seeks to the specified position in the current stream.</summary>
        /// <param name="seconds">Target position in seconds.</param>
        public void Seek(double seconds)
        {
            if (_decodeStream == 0) return;
            long bytePos = Bass.ChannelSeconds2Bytes(_decodeStream, seconds);
            Bass.ChannelSetPosition(_decodeStream, bytePos);
            Interlocked.Exchange(ref _endOfStreamFlag, 0);
        }

        /// <summary>Sets the WASAPI session volume.</summary>
        /// <param name="volume">Volume level from 0.0 (silent) to 1.0 (full). Clamped to range.</param>
        public void SetVolume(float volume)
        {
            float clamped = Math.Max(0.0f, Math.Min(1.0f, volume));
            BassWasapi.SetVolume(WasapiVolumeTypes.Session, clamped);
        }

        /// <summary>Sets the WASAPI session mute state.</summary>
        /// <param name="muted">True to mute, false to unmute.</param>
        public void SetMute(bool muted)
        {
            BassWasapi.SetMute(WasapiVolumeTypes.Session, muted);
        }

        // --- Device enumeration ---

        /// <summary>
        /// Enumerates available WASAPI output devices.
        /// Always includes (-1, "Default Output Device") as the first entry.
        /// Filters to non-input, non-loopback, enabled devices only.
        /// Call once when the settings overlay opens — not in OnPaint.
        /// </summary>
        public List<(int DeviceIndex, string Name)> GetWasapiOutputDevices()
        {
            var result = new List<(int DeviceIndex, string Name)>();

            // Default device always first
            result.Add((-1, "Default Output Device"));

            try
            {
                int count = BassWasapi.DeviceCount;
                for (int i = 0; i < count; i++)
                {
                    WasapiDeviceInfo info = BassWasapi.GetDeviceInfo(i);
                    // Filter: output devices only (not input, not loopback), and must be enabled
                    if (!info.IsInput && !info.IsLoopback && info.IsEnabled)
                    {
                        string name = info.Name ?? $"Device {i}";
                        result.Add((i, name));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[AudioPlayer] GetWasapiOutputDevices failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Switches the WASAPI output device. Stops current playback, tears down
        /// WASAPI + mixer, reinitializes with the new device, and recreates the mixer.
        /// Returns true on success. On failure, attempts to fall back to default device (-1).
        /// </summary>
        public bool SwitchDevice(int deviceIndex)
        {
            if (!_isInitialized) return false;
            if (deviceIndex == _currentDeviceIndex) return true; // no-op

            Logger.Info($"[AudioPlayer] Switching WASAPI device from {_currentDeviceIndex} to {deviceIndex}");

            // 1. Stop current playback and free decode stream
            StopAndFreeStream();

            // 2. Stop and free WASAPI
            BassWasapi.Stop();
            BassWasapi.Free();

            // 3. Free old mixer
            if (_mixerStream != 0)
            {
                Bass.StreamFree(_mixerStream);
                _mixerStream = 0;
            }

            // 4. Reinit WASAPI with new device
            if (!BassWasapi.Init(deviceIndex, 0, 0, WasapiInitFlags.Shared, 0f, 0f, _wasapiProc, IntPtr.Zero))
            {
                Logger.Error($"[AudioPlayer] SwitchDevice: BassWasapi.Init({deviceIndex}) failed: {Bass.LastError}. Falling back to default.");
                // Fallback to default device
                if (!BassWasapi.Init(-1, 0, 0, WasapiInitFlags.Shared, 0f, 0f, _wasapiProc, IntPtr.Zero))
                {
                    Logger.Error($"[AudioPlayer] SwitchDevice: Fallback to default also failed: {Bass.LastError}");
                    _isInitialized = false;
                    return false;
                }
                _currentDeviceIndex = -1;
            }
            else
            {
                _currentDeviceIndex = deviceIndex;
            }

            // 5. Get new device format
            var wasapiInfo = BassWasapi.Info;
            _wasapiFreq = wasapiInfo.Frequency;
            _wasapiChannels = wasapiInfo.Channels;
            Logger.Info($"[AudioPlayer] New WASAPI device: {_wasapiFreq}Hz / {_wasapiChannels}ch");

            // 6. Recreate mixer at new device's sample rate
            _mixerStream = BassMix.CreateMixerStream(
                _wasapiFreq, _wasapiChannels,
                BassFlags.Decode | BassFlags.Float);

            if (_mixerStream == 0)
            {
                Logger.Error($"[AudioPlayer] SwitchDevice: CreateMixerStream failed: {Bass.LastError}");
                BassWasapi.Free();
                _isInitialized = false;
                return false;
            }

            // 7. Restart WASAPI output
            if (!BassWasapi.Start())
            {
                Logger.Error($"[AudioPlayer] SwitchDevice: BassWasapi.Start() failed: {Bass.LastError}");
                Bass.StreamFree(_mixerStream);
                _mixerStream = 0;
                BassWasapi.Free();
                _isInitialized = false;
                return false;
            }

            Logger.Info($"[AudioPlayer] SwitchDevice complete — device {_currentDeviceIndex}");
            return true;
        }

        // --- End-of-stream polling ---

        /// <summary>
        /// Polls the end-of-stream flag set by the WASAPI callback. If the flag is set,
        /// pauses the mixer channel, resets position, and raises <see cref="PlaybackEnded"/>.
        /// Called from the UI timer tick.
        /// </summary>
        public void CheckEndOfStream()
        {
            if (Interlocked.CompareExchange(ref _endOfStreamFlag, 0, 1) == 1)
            {
                if (_decodeStream != 0)
                {
                    BassMix.ChannelFlags(_decodeStream, BassFlags.MixerChanPause, BassFlags.MixerChanPause);
                    Bass.ChannelSetPosition(_decodeStream, 0);
                }
                SetState(AudioPlayerState.Stopped);
                PlaybackEnded?.Invoke(this, EventArgs.Empty);
            }
        }

        // --- WASAPI callback ---

        private int WasapiCallback(IntPtr buffer, int length, IntPtr user)
        {
            // When not playing or no mixer, zero the buffer and return length.
            // Returning length without zeroing causes buzzing from stale buffer data.
            if (_mixerStream == 0 || _state != AudioPlayerState.Playing)
            {
                ZeroMemory(buffer, length);
                return length;
            }

            // Read from the mixer (which handles sample rate conversion from decode stream)
            int bytesRead = Bass.ChannelGetData(_mixerStream, buffer, length);

            if (bytesRead <= 0)
            {
                Interlocked.Exchange(ref _endOfStreamFlag, 1);
                ZeroMemory(buffer, length);
                return length;
            }

            // If mixer returned less than requested, zero the remainder
            if (bytesRead < length)
            {
                ZeroMemory(buffer + bytesRead, length - bytesRead);
            }

            return length;
        }

        // --- Shutdown / disposal ---

        /// <summary>Stops playback and frees the current decode stream and pinned buffer.</summary>
        public void StopAndFreeCurrentStream()
        {
            StopAndFreeStream();
        }

        private void StopAndFreeStream()
        {
            SetState(AudioPlayerState.Idle);

            // Remove from mixer before freeing
            if (_decodeStream != 0 && _mixerStream != 0)
            {
                BassMix.MixerRemoveChannel(_decodeStream);
            }

            // Free BASS stream/music handle first (stream holds a pointer into the pinned buffer)
            if (_decodeStream != 0)
            {
                if (_isModuleHandle)
                    Bass.MusicFree(_decodeStream);
                else
                    Bass.StreamFree(_decodeStream);
                _decodeStream = 0;
                _isModuleHandle = false;
            }

            // Then release the GCHandle
            if (_pinnedBuffer.HasValue)
            {
                _pinnedBuffer.Value.Free();
                _pinnedBuffer = null;
            }

            _totalDurationSeconds = 0;
            Interlocked.Exchange(ref _endOfStreamFlag, 0);
        }

        /// <summary>
        /// Tears down the entire audio engine: frees the current stream, stops WASAPI,
        /// frees the mixer, and releases the BASS library.
        /// </summary>
        public void Shutdown()
        {
            if (!_isInitialized) return;

            StopAndFreeStream();

            BassWasapi.Stop();
            BassWasapi.Free();

            if (_mixerStream != 0)
            {
                Bass.StreamFree(_mixerStream);
                _mixerStream = 0;
            }

            Bass.Free();

            _isInitialized = false;
            Logger.Info("[AudioPlayer] Shutdown complete");
        }

        private void SetState(AudioPlayerState newState)
        {
            _state = newState;
            StateChanged?.Invoke(this, newState);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Shutdown();
        }
    }
}
