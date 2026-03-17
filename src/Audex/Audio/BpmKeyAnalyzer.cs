using System;
using System.Runtime.InteropServices;
using System.Threading;
using ManagedBass;
using ManagedBass.Fx;

namespace Audex.Audio
{
    /// <summary>
    /// Orchestrates BPM detection (via ManagedBass.Fx BPMDecodeGet) then musical key detection
    /// (via tuning-corrected, frame-normalized chroma + key-profile correlation). Runs on the calling thread.
    /// Supports cancellation via CancellationToken and progress reporting 0.0-1.0.
    /// </summary>
    public static class BpmKeyAnalyzer
    {
        /// <summary>
        /// Analyzes audio data for BPM (0-50% progress) then musical key (50-100% progress).
        /// </summary>
        /// <param name="audioData">Raw audio file bytes. Pinned during analysis.</param>
        /// <param name="ct">Cancellation token. Returns null if cancelled during key phase.</param>
        /// <param name="onProgress">Progress callback receiving 0.0-1.0 fraction.</param>
        /// <param name="maxSeconds">Maximum audio duration to analyze. Default: 300 seconds.</param>
        /// <param name="keyProfileType">Key profile strategy: "auto", "krumhansl", or "temperley".</param>
        /// <returns>AnalysisResult with BPM, key, and confidence values; or null if cancelled.</returns>
        public static AnalysisResult? Analyze(byte[] audioData, CancellationToken ct,
            Action<float> onProgress, double maxSeconds = 300.0, string keyProfileType = "auto")
        {
            var result = new AnalysisResult();

            // Pin audioData so the GC cannot move it during BASS decode operations
            GCHandle handle = GCHandle.Alloc(audioData, GCHandleType.Pinned);
            IntPtr ptr = handle.AddrOfPinnedObject();
            int stream = 0;

            try
            {
                // ---- BPM PHASE (0-50% progress) ----

                stream = Bass.CreateStream(ptr, 0, audioData.Length, BassFlags.Decode | BassFlags.Float);
                if (stream == 0)
                {
                    result.BpmFailed = true;
                    result.KeyFailed = true;
                    result.FailureReason = "Failed to create decode stream";
                    return result;
                }

                // Determine analysis end position (clamped to maxSeconds)
                long totalBytes = Bass.ChannelGetLength(stream);
                double totalSec = Bass.ChannelBytes2Seconds(stream, totalBytes);
                double endSec = Math.Min(totalSec, maxSeconds);

                bool cancelled = false;

                // BPM progress callback: maps BPMDecodeGet progress (0-100) to onProgress(0-0.5)
                BPMProgressProcedure bpmCallback = (channel, percent, user) =>
                {
                    if (ct.IsCancellationRequested)
                    {
                        cancelled = true;
                        return; // BPMDecodeGet completes but result will be discarded
                    }
                    onProgress((float)(percent * 0.005)); // 0-100 -> 0.0-0.5
                };

                float bpm = BassFx.BPMDecodeGet(stream, 0.0, endSec, 0, BassFlags.Default, bpmCallback, IntPtr.Zero);

                // Free the BPM stream before creating the key stream
                Bass.StreamFree(stream);
                stream = 0;

                if (cancelled || ct.IsCancellationRequested)
                {
                    handle.Free();
                    return null;
                }

                if (bpm > 0)
                {
                    result.DetectedBpm = (int)Math.Round(bpm);
                    // Confidence heuristic: 0.92 for typical DJ range, 0.70 for extremes
                    result.BpmConfidence = (bpm >= 60f && bpm <= 200f) ? 0.92f : 0.70f;
                }
                else
                {
                    result.BpmFailed = true;
                    result.BpmConfidence = 0f;
                }

                // ---- KEY PHASE (50-100% progress) ----

                // Create a fresh decode stream from the same pinned data
                stream = Bass.CreateStream(ptr, 0, audioData.Length, BassFlags.Decode | BassFlags.Float);
                if (stream == 0)
                {
                    result.KeyFailed = true;
                    if (result.BpmFailed)
                        result.FailureReason = "Failed to create decode stream for key analysis";
                    return result;
                }

                // Get channel info for sample rate
                Bass.ChannelGetInfo(stream, out ChannelInfo info);
                int sampleRate = info.Frequency;

                int fftSize = 4096;
                int fftBins = fftSize / 2;

                // Pre-compute frequency per FFT bin once.
                double[] binFrequencies = new double[fftBins];
                for (int bin = 0; bin < fftBins; bin++)
                {
                    binFrequencies[bin] = (double)bin * sampleRate / fftSize;
                }

                double[] chroma = new double[12];
                double[] frameChroma = new double[12];
                float[] fftBuffer = new float[fftSize];

                long keyTotalBytes = Bass.ChannelGetLength(stream);
                double keyEndSec = Math.Min(Bass.ChannelBytes2Seconds(stream, keyTotalBytes), maxSeconds);
                long keyEndBytes = Bass.ChannelSeconds2Bytes(stream, keyEndSec);

                int frameCount = 0;
                float lastProgressPct = 0.5f;
                const float progressReportThreshold = 0.02f; // Report every 2%

                // Running detuning estimate in semitones. Starts at 0 and converges.
                double detuneWeightedSum = 0.0;
                double detuneWeightTotal = 0.0;
                double tuningSemitoneOffset = 0.0;
                double tuningCorrectionFactor = 1.0;

                while (true)
                {
                    if (frameCount % 100 == 0 && ct.IsCancellationRequested)
                    {
                        Bass.StreamFree(stream);
                        stream = 0;
                        handle.Free();
                        return null;
                    }

                    // Check if we've exceeded maxSeconds
                    long currentPos = Bass.ChannelGetPosition(stream);
                    if (currentPos >= keyEndBytes || currentPos < 0)
                        break;

                    // Get FFT data (DataFlags.FFT4096 = complex 4096-point FFT, returns 2048 magnitude bins)
                    int bytesRead = Bass.ChannelGetData(stream, fftBuffer, (int)DataFlags.FFT4096);
                    if (bytesRead < 0)
                        break; // End of stream or error

                    // Estimate global tuning offset from stable, strong bins.
                    AccumulateDetuningEstimate(fftBuffer, binFrequencies, ref detuneWeightedSum, ref detuneWeightTotal);
                    if (detuneWeightTotal > 0.0 && frameCount >= 8)
                    {
                        tuningSemitoneOffset = Clamp(detuneWeightedSum / detuneWeightTotal, -0.5, 0.5);
                        tuningCorrectionFactor = Math.Pow(2.0, -tuningSemitoneOffset / 12.0);
                    }

                    // Build per-frame chroma with interpolation + frame normalization (HPCP-like).
                    AccumulateFrameChroma(fftBuffer, binFrequencies, tuningCorrectionFactor, frameChroma);

                    for (int i = 0; i < 12; i++)
                        chroma[i] += frameChroma[i];

                    frameCount++;

                    // Report progress every ~2%
                    long denom = keyEndBytes > 0 ? keyEndBytes : 1;
                    float progress = 0.5f + 0.5f * Math.Min(1.0f, (float)currentPos / denom);
                    if (progress - lastProgressPct >= progressReportThreshold)
                    {
                        onProgress(progress);
                        lastProgressPct = progress;
                    }
                }

                // Final 100% progress
                onProgress(1.0f);

                // Detect key from accumulated chromagram
                var (key, keyConf) = KeyDetector.DetectKeyFromChromagram(chroma, keyProfileType);

                if (string.IsNullOrEmpty(key) || key == "\u2014")
                {
                    result.KeyFailed = true;
                    result.KeyConfidence = 0f;
                }
                else
                {
                    result.DetectedKey = key;
                    result.KeyConfidence = keyConf;
                }

                if (result.BpmFailed && result.KeyFailed)
                    result.FailureReason = "unable to detect";

                return result;
            }
            finally
            {
                // Always free stream before handle (critical: avoid pointer-into-freed-memory)
                if (stream != 0)
                {
                    Bass.StreamFree(stream);
                    stream = 0;
                }
                if (handle.IsAllocated)
                    handle.Free();
            }
        }

        private static void AccumulateDetuningEstimate(float[] fftBuffer, double[] binFrequencies,
            ref double detuneWeightedSum, ref double detuneWeightTotal)
        {
            int maxBin = Math.Min(fftBuffer.Length / 2, binFrequencies.Length);
            for (int bin = 2; bin < maxBin; bin++)
            {
                float magnitude = fftBuffer[bin];
                if (magnitude < 1e-5f)
                    continue;

                double freq = binFrequencies[bin];
                if (freq < 55.0 || freq > 1760.0)
                    continue;

                double midi = 69.0 + 12.0 * Math.Log(freq / 440.0, 2.0);
                double nearest = Math.Round(midi);
                double detune = midi - nearest;

                if (detune > 0.5) detune -= 1.0;
                if (detune < -0.5) detune += 1.0;

                double weight = magnitude * magnitude;
                detuneWeightedSum += detune * weight;
                detuneWeightTotal += weight;
            }
        }

        private static void AccumulateFrameChroma(float[] fftBuffer, double[] binFrequencies,
            double tuningCorrectionFactor, double[] frameChroma)
        {
            Array.Clear(frameChroma, 0, frameChroma.Length);

            int maxBin = Math.Min(fftBuffer.Length / 2, binFrequencies.Length);
            for (int bin = 2; bin < maxBin; bin++)
            {
                float magnitude = fftBuffer[bin];
                if (magnitude <= 0f)
                    continue;

                double correctedFreq = binFrequencies[bin] * tuningCorrectionFactor;
                if (correctedFreq < 27.5 || correctedFreq > 5000.0)
                    continue;

                double pitchClass = KeyDetector.FreqToPitchClassFloat(correctedFreq);
                if (pitchClass < 0.0)
                    continue;

                int lowerPc = (int)Math.Floor(pitchClass);
                int upperPc = (lowerPc + 1) % 12;
                double fraction = pitchClass - lowerPc;

                // Magnitude compression improves robustness by reducing dominance of outlier peaks.
                double weight = Math.Sqrt(magnitude);
                frameChroma[lowerPc] += weight * (1.0 - fraction);
                frameChroma[upperPc] += weight * fraction;
            }

            double frameEnergy = 0.0;
            for (int i = 0; i < 12; i++)
                frameEnergy += frameChroma[i];

            if (frameEnergy > 1e-12)
            {
                for (int i = 0; i < 12; i++)
                    frameChroma[i] /= frameEnergy;
            }
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
