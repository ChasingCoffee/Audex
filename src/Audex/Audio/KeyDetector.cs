using System;
using System.Collections.Generic;

namespace Audex.Audio
{
    /// <summary>
    /// Krumhansl-Schmuckler key detection from a 12-bin chromagram.
    /// Uses Pearson correlation against major and minor key profiles (Krumhansl 1990).
    /// </summary>
    public static class KeyDetector
    {
        private const string KrumhanslProfileName = "krumhansl";
        private const string TemperleyProfileName = "temperley";
        private const string AutoProfileName = "auto";

        // Krumhansl-Schmuckler profiles (C root, 1990)
        private static readonly double[] KrumhanslMajorProfile =
            { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
        private static readonly double[] KrumhanslMinorProfile =
            { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };

        // Temperley profiles (C root). Useful alternative for modern/pop-heavy material.
        private static readonly double[] TemperleyMajorProfile =
            { 5.0, 2.0, 3.5, 2.0, 4.5, 4.0, 2.0, 4.5, 2.0, 3.5, 1.5, 4.0 };
        private static readonly double[] TemperleyMinorProfile =
            { 5.0, 2.0, 3.5, 4.5, 2.0, 4.0, 2.0, 4.5, 3.5, 2.0, 1.5, 4.0 };

        // Standard enharmonic spelling for major keys (C=0)
        private static readonly string[] MajorKeys = { "C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };

        // Standard enharmonic spelling for minor keys (Cm=0)
        private static readonly string[] MinorKeys = { "Cm", "C#m", "Dm", "Ebm", "Em", "Fm", "F#m", "Gm", "Abm", "Am", "Bbm", "Bm" };

        /// <summary>
        /// Detects the musical key from a 12-bin chromagram using Krumhansl-Schmuckler correlation.
        /// Returns ("--", 0f) if the chromagram is silent (sum below threshold).
        /// </summary>
        /// <param name="chroma">12-element chromagram array (C=0 through B=11). May be un-normalized.</param>
        /// <returns>Tuple of (key string in standard notation, confidence 0.0-1.0).</returns>
        public static (string key, float confidence) DetectKeyFromChromagram(double[] chroma)
        {
            // Preserve legacy behavior for existing callers/tests.
            return DetectKeyFromChromagram(chroma, KrumhanslProfileName);
        }

        /// <summary>
        /// Detects musical key from a 12-bin chromagram with selectable key-profile set.
        /// profileType supports: "krumhansl", "temperley", "auto".
        /// In "auto", both profile sets are evaluated and the highest-scoring key is selected.
        /// </summary>
        public static (string key, float confidence) DetectKeyFromChromagram(double[] chroma, string? profileType)
        {
            if (chroma == null || chroma.Length < 12)
                return ("\u2014", 0f);

            // Normalize chromagram to sum=1
            double sum = 0.0;
            for (int i = 0; i < 12; i++)
                sum += chroma[i];

            if (sum < 1e-10)
                return ("\u2014", 0f); // em dash — silent audio

            double[] normalized = new double[12];
            for (int i = 0; i < 12; i++)
                normalized[i] = chroma[i] / sum;

            double bestCorr = double.MinValue;
            double secondBestCorr = double.MinValue;
            string bestKey = "\u2014";

            string normalizedProfile = NormalizeProfileType(profileType);
            IReadOnlyList<KeyProfileSet> activeProfiles = GetActiveProfiles(normalizedProfile);

            // Evaluate all 24 keys for each active profile set.
            foreach (KeyProfileSet profile in activeProfiles)
            {
                for (int root = 0; root < 12; root++)
                {
                    double majorCorr = PearsonCorrelation(normalized, RotateProfile(profile.MajorProfile, root));
                    double minorCorr = PearsonCorrelation(normalized, RotateProfile(profile.MinorProfile, root));

                    UpdateTopCandidates(majorCorr, MajorKeys[root], ref bestCorr, ref secondBestCorr, ref bestKey);
                    UpdateTopCandidates(minorCorr, MinorKeys[root], ref bestCorr, ref secondBestCorr, ref bestKey);
                }
            }

            if (bestCorr == double.MinValue)
                return ("\u2014", 0f);

            // Confidence combines absolute score and margin over runner-up.
            // This reduces false certainty on ambiguous tracks.
            double corrConfidence = Clamp01((bestCorr + 1.0) / 2.0);
            double gap = secondBestCorr == double.MinValue ? 1.0 : Math.Max(0.0, bestCorr - secondBestCorr);
            double gapConfidence = Clamp01(gap / 0.20); // 0.20+ margin considered decisive.
            float confidence = (float)Clamp01(corrConfidence * 0.75 + gapConfidence * 0.25);

            return (bestKey, confidence);
        }

        /// <summary>
        /// Computes Pearson correlation coefficient between two arrays of length 12.
        /// Returns 0.0 if either array has zero variance.
        /// </summary>
        private static double PearsonCorrelation(double[] x, double[] y)
        {
            const int n = 12;

            double sumX = 0.0, sumY = 0.0;
            for (int i = 0; i < n; i++)
            {
                sumX += x[i];
                sumY += y[i];
            }

            double meanX = sumX / n;
            double meanY = sumY / n;

            double cov = 0.0, varX = 0.0, varY = 0.0;
            for (int i = 0; i < n; i++)
            {
                double dx = x[i] - meanX;
                double dy = y[i] - meanY;
                cov += dx * dy;
                varX += dx * dx;
                varY += dy * dy;
            }

            double denom = Math.Sqrt(varX * varY);
            return denom < 1e-14 ? 0.0 : cov / denom;
        }

        /// <summary>
        /// Rotates a 12-element profile array right by <paramref name="semitones"/> positions.
        /// Index i of result = profile[(i - semitones + 12) % 12].
        /// </summary>
        private static double[] RotateProfile(double[] profile, int semitones)
        {
            double[] rotated = new double[12];
            for (int i = 0; i < 12; i++)
                rotated[i] = profile[(i - semitones + 12) % 12];
            return rotated;
        }

        private static void UpdateTopCandidates(double correlation, string key,
            ref double bestCorrelation, ref double secondBestCorrelation, ref string bestKey)
        {
            if (correlation > bestCorrelation)
            {
                secondBestCorrelation = bestCorrelation;
                bestCorrelation = correlation;
                bestKey = key;
            }
            else if (correlation > secondBestCorrelation)
            {
                secondBestCorrelation = correlation;
            }
        }

        private static IReadOnlyList<KeyProfileSet> GetActiveProfiles(string profileType)
        {
            if (string.Equals(profileType, TemperleyProfileName, StringComparison.Ordinal))
            {
                return new[] { new KeyProfileSet(TemperleyMajorProfile, TemperleyMinorProfile) };
            }

            if (string.Equals(profileType, KrumhanslProfileName, StringComparison.Ordinal))
            {
                return new[] { new KeyProfileSet(KrumhanslMajorProfile, KrumhanslMinorProfile) };
            }

            // auto/unknown -> evaluate both and choose the strongest match.
            return new[]
            {
                new KeyProfileSet(KrumhanslMajorProfile, KrumhanslMinorProfile),
                new KeyProfileSet(TemperleyMajorProfile, TemperleyMinorProfile)
            };
        }

        private static string NormalizeProfileType(string? profileType)
        {
            if (string.IsNullOrWhiteSpace(profileType))
                return AutoProfileName;
            return profileType!.Trim().ToLowerInvariant();
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }

        /// <summary>
        /// Converts a frequency in Hz to a MIDI pitch class (0=C, 1=C#, ..., 11=B).
        /// Returns -1 if frequency is zero or negative.
        /// </summary>
        public static int FreqToPitchClass(double freqHz)
        {
            if (freqHz <= 0.0)
                return -1;

            // MIDI note = 69 + 12 * log2(freq / 440)
            double midi = 69.0 + 12.0 * Math.Log(freqHz / 440.0, 2.0);
            int pitchClass = ((int)Math.Round(midi)) % 12;
            if (pitchClass < 0)
                pitchClass += 12;
            return pitchClass;
        }

        /// <summary>
        /// Converts a frequency in Hz to continuous pitch class in [0, 12).
        /// Returns -1 if frequency is zero or negative.
        /// </summary>
        public static double FreqToPitchClassFloat(double freqHz)
        {
            if (freqHz <= 0.0)
                return -1.0;

            double midi = 69.0 + 12.0 * Math.Log(freqHz / 440.0, 2.0);
            double pitchClass = midi % 12.0;
            return pitchClass < 0.0 ? pitchClass + 12.0 : pitchClass;
        }

        private readonly struct KeyProfileSet
        {
            public KeyProfileSet(double[] majorProfile, double[] minorProfile)
            {
                MajorProfile = majorProfile;
                MinorProfile = minorProfile;
            }

            public double[] MajorProfile { get; }
            public double[] MinorProfile { get; }
        }
    }
}
