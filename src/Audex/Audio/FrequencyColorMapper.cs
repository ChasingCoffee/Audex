using System;
using System.Drawing;

namespace Audex.Audio
{
    /// <summary>
    /// Converts FFT magnitude data into a single Color per waveform bar.
    /// Uses Serato-style independent channel mapping: R=bass, G=mids, B=highs.
    /// Each channel scales independently with its band's energy, enabling
    /// purple (bass+highs), yellow (bass+mids), cyan (mids+highs), white (all).
    /// Crossovers: 200 Hz, 1500 Hz (matching Serato).
    /// </summary>
    public static class FrequencyColorMapper
    {
        // FFT window size (tweakable but not user-facing)
        public const int FftWindowSize = 2048;

        // Crossover frequencies (matching Serato: ~200 Hz, ~1500 Hz)
        private const float BassLowHz = 20f;
        private const float BassMidHz = 200f;
        private const float MidHighHz = 1500f;
        private const float HighMaxHz = 16000f;

        // Energy threshold — below this, render neutral/gray
        private const float EnergyThreshold = 0.008f;

        // Perceptual weights to compensate for natural spectral tilt
        private const float BassWeight = 1.0f;
        private const float MidsWeight = 2.5f;
        private const float HighWeight = 5.0f;

        // Gamma curve for visual contrast (< 1.0 = brighter midtones, higher = less saturated)
        private const double Gamma = 0.7;

        /// <summary>
        /// Converts a frequency in Hz to the corresponding FFT bin index.
        /// Uses the standard DSP formula: bin = (int)(hz * fftSize / sampleRate).
        /// Computed at runtime to handle 44100, 48000, 22050 Hz etc.
        /// </summary>
        public static int FreqToBin(float hz, int sampleRate, int fftSize)
        {
            return (int)(hz * fftSize / sampleRate);
        }

        /// <summary>
        /// Computes the RMS energy of a frequency band in the FFT magnitude array.
        /// Uses power-weighted summation: sum of squared magnitudes / count, then sqrt.
        /// Skips bin 0 (DC component).
        /// </summary>
        public static float BandRms(float[] fft, int start, int end)
        {
            start = Math.Max(1, start); // Skip DC bin (index 0)
            if (start >= end || fft == null || end > fft.Length)
                return 0f;

            int count = end - start;
            double sumSquares = 0.0;
            for (int i = start; i < end; i++)
            {
                double v = fft[i];
                sumSquares += v * v;
            }
            return (float)Math.Sqrt(sumSquares / count);
        }

        /// <summary>
        /// Computes a Color for a single waveform bar from FFT magnitude data.
        /// Uses independent channel mapping: R=bass, G=mids, B=highs.
        /// Each channel scales independently, enabling additive color mixing
        /// (purple = bass+highs, yellow = bass+mids, cyan = mids+highs, white = all).
        /// </summary>
        public static Color Compute(float[] fft, int sampleRate, int fftSize, bool isDark)
        {
            if (fft == null || fft.Length == 0 || sampleRate <= 0)
                return NeutralColor(isDark);

            // Compute bin boundaries at runtime (handles 44100, 48000, 22050, etc.)
            int bassLowBin  = FreqToBin(BassLowHz,  sampleRate, fftSize);
            int bassMidBin  = FreqToBin(BassMidHz,  sampleRate, fftSize);
            int midHighBin  = FreqToBin(MidHighHz,  sampleRate, fftSize);
            int highMaxBin  = FreqToBin(HighMaxHz,  sampleRate, fftSize);

            // Clamp bins to valid range
            int maxBin = Math.Min(fft.Length, fftSize / 2);
            bassLowBin = Math.Max(1, Math.Min(bassLowBin, maxBin - 1));
            bassMidBin = Math.Max(bassLowBin + 1, Math.Min(bassMidBin, maxBin - 1));
            midHighBin = Math.Max(bassMidBin + 1, Math.Min(midHighBin, maxBin - 1));
            highMaxBin = Math.Max(midHighBin + 1, Math.Min(highMaxBin, maxBin));

            // Compute RMS energy per band
            float bassEnergy = BandRms(fft, bassLowBin, bassMidBin);
            float midsEnergy = BandRms(fft, bassMidBin, midHighBin);
            float highEnergy = BandRms(fft, midHighBin, highMaxBin);

            float totalEnergy = bassEnergy + midsEnergy + highEnergy;

            // Below threshold: render neutral gray
            if (totalEnergy < EnergyThreshold)
                return NeutralColor(isDark);

            // Apply perceptual weights to compensate for natural spectral tilt
            float bassW = bassEnergy * BassWeight;
            float midsW = midsEnergy * MidsWeight;
            float highW = highEnergy * HighWeight;

            // Normalize each channel independently to [0, 1] relative to max band.
            // This means the dominant band is always 1.0 (fully bright),
            // and other bands scale relative to it — enabling additive mixing.
            float peak = Math.Max(bassW, Math.Max(midsW, highW));
            if (peak < 1e-8f)
                return NeutralColor(isDark);

            float bassLevel = bassW / peak;
            float midsLevel = midsW / peak;
            float highLevel = highW / peak;

            // Gamma curve: compress toward bright for more vivid colors
            bassLevel = (float)Math.Pow(bassLevel, Gamma);
            midsLevel = (float)Math.Pow(midsLevel, Gamma);
            highLevel = (float)Math.Pow(highLevel, Gamma);

            // Independent channel mapping: R=bass, G=mids, B=highs
            int ceiling = isDark ? 200 : 170;
            int r = (int)(ceiling * bassLevel);
            int g = (int)(ceiling * midsLevel);
            int b = (int)(ceiling * highLevel);

            r = Math.Max(0, Math.Min(255, r));
            g = Math.Max(0, Math.Min(255, g));
            b = Math.Max(0, Math.Min(255, b));

            return Color.FromArgb(r, g, b);
        }

        /// <summary>
        /// Returns the neutral gray color used for below-threshold (silent/near-silent) bars.
        /// Dark: rgb(60, 60, 65), Light: rgb(180, 180, 185)
        /// </summary>
        public static Color NeutralColor(bool isDark)
        {
            return isDark
                ? Color.FromArgb(60, 60, 65)
                : Color.FromArgb(180, 180, 185);
        }

        /// <summary>
        /// Applies a 3-tap moving average to smooth color transitions between adjacent bars.
        /// In-place modification using a temp array to avoid cascading artifacts.
        /// Skips the first and last bar (no neighbors on one side).
        /// </summary>
        public static void SmoothColors(Color[] colors)
        {
            if (colors == null || colors.Length < 3)
                return;

            // Process into temp to avoid cascading averaging artifacts
            Color[] temp = new Color[colors.Length];
            Array.Copy(colors, temp, colors.Length);

            for (int i = 1; i < colors.Length - 1; i++)
            {
                int r = (colors[i - 1].R + colors[i].R + colors[i + 1].R) / 3;
                int g = (colors[i - 1].G + colors[i].G + colors[i + 1].G) / 3;
                int b = (colors[i - 1].B + colors[i].B + colors[i + 1].B) / 3;
                temp[i] = Color.FromArgb(r, g, b);
            }

            // Copy back
            Array.Copy(temp, colors, colors.Length);
        }
    }
}
