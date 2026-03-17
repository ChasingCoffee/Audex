using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using ManagedBass;
using Audex.UI;

namespace Audex.Audio
{
    /// <summary>
    /// Result of a waveform generation pass: peak amplitude array and optional frequency color array.
    /// </summary>
    public class WaveformData
    {
        /// <summary>
        /// Per-bar peak amplitude values (0.0 – 1.0), length up to 2000 bars.
        /// </summary>
        public float[] Peaks { get; set; } = Array.Empty<float>();

        /// <summary>
        /// Per-bar frequency blend colors, parallel to Peaks.
        /// Null if color analysis was not performed or failed.
        /// </summary>
        public Color[]? FrequencyColors { get; set; }
    }

    /// <summary>
    /// Decodes audio data to a canonical peak array (and optional frequency color array) for waveform display.
    /// Uses a separate BASS decode-only stream — does NOT interact with the playback mixer.
    /// </summary>
    public static class WaveformGenerator
    {
        // Target number of bars in the canonical peak array (renderer downsamples at paint time).
        private const int TargetBars = 2000;

        // Read buffer size in floats (4096 floats = 16384 bytes per chunk)
        private const int ChunkFloats = 4096;

        /// <summary>
        /// Decodes audio data and extracts a peak array (and frequency colors) for waveform display.
        /// Runs on the calling thread — the caller is responsible for running this on a background thread.
        /// </summary>
        /// <param name="audioData">Raw audio file bytes (pinned during decode).</param>
        /// <param name="ct">Cancellation token — checked per chunk. Returns null if cancelled.</param>
        /// <param name="isModuleFormat">True for tracked-music module formats (MOD/XM/IT/S3M) — uses MusicLoad and skips FFT.</param>
        /// <param name="onBarReady">Optional progress callback: (barIndex, peakValue) after each bar is computed.</param>
        /// <returns>WaveformData with Peaks and FrequencyColors, or null if decode fails or cancelled.</returns>
        public static WaveformData? Generate(byte[] audioData, CancellationToken ct,
            bool isModuleFormat = false, Action<int, float>? onBarReady = null)
        {
            if (audioData == null || audioData.Length == 0)
                return null;

            GCHandle handle = GCHandle.Alloc(audioData, GCHandleType.Pinned);
            int waveStream = 0;

            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();

                if (isModuleFormat)
                {
                    // Module formats use MusicLoad — MusicLoad copies data so the GCHandle
                    // is only needed to keep the byte[] alive during the call, but since
                    // we release handle at the end either way, keep the pattern uniform.
                    waveStream = Bass.MusicLoad(audioData, 0, audioData.Length,
                        BassFlags.Decode | BassFlags.Float | BassFlags.Prescan, 0);
                }
                else
                {
                    // Create a pure decode stream — does NOT add to any mixer or WASAPI output
                    waveStream = Bass.CreateStream(ptr, 0, audioData.Length, BassFlags.Decode | BassFlags.Float);
                }

                if (waveStream == 0)
                {
                    handle.Free();
                    return null;
                }

                // Get total byte length and channel info
                long totalBytes = Bass.ChannelGetLength(waveStream);
                if (totalBytes <= 0)
                {
                    if (isModuleFormat) Bass.MusicFree(waveStream); else Bass.StreamFree(waveStream);
                    waveStream = 0;
                    handle.Free();
                    return null;
                }

                Bass.ChannelGetInfo(waveStream, out ChannelInfo info);
                int channelCount = Math.Max(1, info.Channels);
                int sampleRate = info.Frequency;

                // Total sample frames = totalBytes / sizeof(float) / channelCount
                long totalSampleFrames = totalBytes / sizeof(float) / channelCount;

                // Clamp target bars to actual sample frame count for very short files
                int targetBars = (int)Math.Min(TargetBars, totalSampleFrames);
                if (targetBars <= 0)
                {
                    Bass.StreamFree(waveStream);
                    waveStream = 0;
                    handle.Free();
                    return null;
                }

                // samplesPerBar is the number of sample FRAMES per bar
                long samplesPerBar = Math.Max(1L, totalSampleFrames / targetBars);

                List<float> peaks = new List<float>(targetBars);
                Color[] colors = new Color[targetBars];
                float[] buffer = new float[ChunkFloats];

                // FFT buffer: FftWindowSize / 2 floats for FFT2048
                float[] fftBuffer = new float[FrequencyColorMapper.FftWindowSize / 2];

                // Cache theme once before loop to avoid per-bar registry reads
                bool isDark = ThemeHelper.IsSystemInDarkMode();

                float barPeak = 0f;
                long samplesInBar = 0;
                int barIndex = 0;

                // samplesProcessed tracks absolute sample frames consumed across all chunks
                // sampleOffsetInChunk is the float index within the current chunk
                // Each "sample frame" spans channelCount floats in the buffer

                while (true)
                {
                    // Check for cancellation before each chunk read
                    if (ct.IsCancellationRequested)
                    {
                        if (isModuleFormat) Bass.MusicFree(waveStream); else Bass.StreamFree(waveStream);
                        waveStream = 0;
                        handle.Free();
                        return null;
                    }

                    // Read up to ChunkFloats floats from the stream
                    int bytesRead = Bass.ChannelGetData(waveStream, buffer, buffer.Length * sizeof(float));
                    if (bytesRead <= 0)
                        break; // end of stream or error

                    int floatsRead = bytesRead / sizeof(float);

                    // Process floats: group by channelCount to get sample frames,
                    // track peak per bar
                    for (int i = 0; i < floatsRead; )
                    {
                        // Read one sample frame (channelCount floats), take max abs across channels
                        float framePeak = 0f;
                        for (int ch = 0; ch < channelCount && i < floatsRead; ch++, i++)
                        {
                            float v = Math.Abs(buffer[i]);
                            if (v > framePeak) framePeak = v;
                        }

                        if (framePeak > barPeak) barPeak = framePeak;
                        samplesInBar++;

                        // When we've accumulated enough sample frames for one bar, emit it
                        if (samplesInBar >= samplesPerBar)
                        {
                            float peakValue = Math.Min(1f, barPeak);
                            peaks.Add(peakValue);
                            onBarReady?.Invoke(barIndex, peakValue);

                            // Interleaved FFT read: get frequency data for the last decoded block.
                            // Skipped for module formats — they produce mono-color waveforms only.
                            if (!isModuleFormat)
                            {
                                int fftBytesRead = Bass.ChannelGetData(waveStream, fftBuffer, (int)DataFlags.FFT2048);
                                if (fftBytesRead > 0)
                                {
                                    colors[barIndex] = FrequencyColorMapper.Compute(
                                        fftBuffer, sampleRate, FrequencyColorMapper.FftWindowSize, isDark);
                                }
                                else
                                {
                                    colors[barIndex] = FrequencyColorMapper.NeutralColor(isDark);
                                }
                            }

                            barIndex++;
                            barPeak = 0f;
                            samplesInBar = 0;

                            // Stop if we've hit our target bar count
                            if (barIndex >= targetBars)
                                goto done;
                        }
                    }
                }

                done:
                // Flush any remaining partial bar as a final peak
                if (samplesInBar > 0 && barIndex < targetBars)
                {
                    float peakValue = Math.Min(1f, barPeak);
                    peaks.Add(peakValue);
                    onBarReady?.Invoke(barIndex, peakValue);

                    // FFT for the last partial bar — skipped for module formats
                    if (!isModuleFormat)
                    {
                        int fftBytesRead = Bass.ChannelGetData(waveStream, fftBuffer, (int)DataFlags.FFT2048);
                        if (fftBytesRead > 0)
                        {
                            colors[barIndex] = FrequencyColorMapper.Compute(
                                fftBuffer, sampleRate, FrequencyColorMapper.FftWindowSize, isDark);
                        }
                        else
                        {
                            colors[barIndex] = FrequencyColorMapper.NeutralColor(isDark);
                        }
                    }
                }

                // Apply 3-tap neighbor smoothing for color transitions — not for module formats
                if (!isModuleFormat)
                    FrequencyColorMapper.SmoothColors(colors);

                // Trim colors array to match peaks count
                float[] peakArray = peaks.ToArray();

                // Module formats return null FrequencyColors — renderer falls back to mono-color
                Color[]? colorResult = null;
                if (!isModuleFormat)
                {
                    Color[] colorArray = new Color[peakArray.Length];
                    Array.Copy(colors, colorArray, peakArray.Length);
                    colorResult = colorArray;
                }

                // CRITICAL: Free stream BEFORE handle.Free to avoid BASS accessing freed memory
                if (isModuleFormat) Bass.MusicFree(waveStream); else Bass.StreamFree(waveStream);
                waveStream = 0;
                handle.Free();

                return new WaveformData
                {
                    Peaks = peakArray,
                    FrequencyColors = colorResult
                };
            }
            catch
            {
                // Ensure cleanup even on unexpected exception
                if (waveStream != 0)
                {
                    if (isModuleFormat) Bass.MusicFree(waveStream); else Bass.StreamFree(waveStream);
                    waveStream = 0;
                }
                handle.Free();
                return null;
            }
        }
    }
}
