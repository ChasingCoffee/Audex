using System;
using Audex.Audio;
using FluentAssertions;
using Xunit;

namespace Audex.Tests
{
    public class KeyDetectorTests
    {
        // ── FreqToPitchClass ──

        [Fact]
        public void FreqToPitchClass_A440_Returns9()
        {
            // A4 = 440 Hz, MIDI 69, pitch class 9 (A)
            KeyDetector.FreqToPitchClass(440.0).Should().Be(9);
        }

        [Fact]
        public void FreqToPitchClass_MiddleC_Returns0()
        {
            // C4 ≈ 261.63 Hz, MIDI 60, pitch class 0 (C)
            KeyDetector.FreqToPitchClass(261.63).Should().Be(0);
        }

        [Theory]
        [InlineData(261.63, 0)]  // C4
        [InlineData(277.18, 1)]  // C#4
        [InlineData(293.66, 2)]  // D4
        [InlineData(311.13, 3)]  // Eb4
        [InlineData(329.63, 4)]  // E4
        [InlineData(349.23, 5)]  // F4
        [InlineData(369.99, 6)]  // F#4
        [InlineData(392.00, 7)]  // G4
        [InlineData(415.30, 8)]  // Ab4
        [InlineData(440.00, 9)]  // A4
        [InlineData(466.16, 10)] // Bb4
        [InlineData(493.88, 11)] // B4
        public void FreqToPitchClass_AllTwelveNotes(double freqHz, int expectedPitchClass)
        {
            KeyDetector.FreqToPitchClass(freqHz).Should().Be(expectedPitchClass);
        }

        [Fact]
        public void FreqToPitchClass_OctaveEquivalence()
        {
            // A2 = 110 Hz, A4 = 440 Hz, A6 = 1760 Hz — all pitch class 9
            KeyDetector.FreqToPitchClass(110.0).Should().Be(9);
            KeyDetector.FreqToPitchClass(440.0).Should().Be(9);
            KeyDetector.FreqToPitchClass(1760.0).Should().Be(9);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(-440.0)]
        public void FreqToPitchClass_InvalidFrequency_ReturnsMinus1(double freq)
        {
            KeyDetector.FreqToPitchClass(freq).Should().Be(-1);
        }

        // ── DetectKeyFromChromagram: silent audio ──

        [Fact]
        public void DetectKey_SilentChromagram_ReturnsEmDashAndZeroConfidence()
        {
            var silent = new double[12]; // all zeros
            var (key, confidence) = KeyDetector.DetectKeyFromChromagram(silent);

            key.Should().Be("\u2014"); // em dash
            confidence.Should().Be(0f);
        }

        [Fact]
        public void DetectKey_NearSilentChromagram_ReturnsEmDash()
        {
            var nearSilent = new double[12];
            nearSilent[0] = 1e-11; // below 1e-10 threshold
            var (key, confidence) = KeyDetector.DetectKeyFromChromagram(nearSilent);

            key.Should().Be("\u2014");
            confidence.Should().Be(0f);
        }

        // ── DetectKeyFromChromagram: known keys ──

        [Fact]
        public void DetectKey_PureCMajorChromagram_ReturnsC()
        {
            // Spike energy on C (0), E (4), G (7) — the C major triad
            var chroma = new double[12];
            chroma[0] = 1.0; // C
            chroma[4] = 0.8; // E
            chroma[7] = 0.6; // G

            var (key, confidence) = KeyDetector.DetectKeyFromChromagram(chroma);

            key.Should().Be("C");
            confidence.Should().BeGreaterThan(0.5f);
        }

        [Fact]
        public void DetectKey_PureAMinorChromagram_ReturnsAm()
        {
            // Spike energy on A (9), C (0), E (4) — the A minor triad
            var chroma = new double[12];
            chroma[9] = 1.0; // A
            chroma[0] = 0.8; // C
            chroma[4] = 0.6; // E

            var (key, confidence) = KeyDetector.DetectKeyFromChromagram(chroma);

            key.Should().Be("Am");
            confidence.Should().BeGreaterThan(0.5f);
        }

        [Fact]
        public void DetectKey_PureGMajorChromagram_ReturnsG()
        {
            // G major triad: G (7), B (11), D (2)
            var chroma = new double[12];
            chroma[7] = 1.0;  // G
            chroma[11] = 0.8; // B
            chroma[2] = 0.6;  // D

            var (key, confidence) = KeyDetector.DetectKeyFromChromagram(chroma);

            key.Should().Be("G");
            confidence.Should().BeGreaterThan(0.5f);
        }

        [Fact]
        public void DetectKey_KrumhanslMajorProfile_ReturnsCMajor()
        {
            // Feed the major profile itself — should perfectly correlate with C major
            var majorProfile = new double[] { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };

            var (key, confidence) = KeyDetector.DetectKeyFromChromagram(majorProfile);

            key.Should().Be("C");
            confidence.Should().BeGreaterThan(0.9f);
        }

        [Fact]
        public void DetectKey_KrumhanslMinorProfile_ReturnsCm()
        {
            // Feed the minor profile itself — should perfectly correlate with C minor
            var minorProfile = new double[] { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };

            var (key, confidence) = KeyDetector.DetectKeyFromChromagram(minorProfile);

            key.Should().Be("Cm");
            confidence.Should().BeGreaterThan(0.9f);
        }

        // ── Confidence range ──

        [Fact]
        public void DetectKey_Confidence_AlwaysBetween0And1()
        {
            // Uniform chromagram — low confidence expected
            var uniform = new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
            var (_, confidence) = KeyDetector.DetectKeyFromChromagram(uniform);

            confidence.Should().BeInRange(0f, 1f);
        }

        [Fact]
        public void DetectKey_UniformChromagram_LowConfidence()
        {
            // Uniform energy across all pitch classes — no clear key
            var uniform = new double[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
            var (_, confidence) = KeyDetector.DetectKeyFromChromagram(uniform);

            // With uniform input, Pearson correlation should be near 0,
            // mapped to confidence near 0.5
            confidence.Should().BeLessThan(0.7f);
        }

        [Fact]
        public void DetectKey_KrumhanslProfileExplicit_ReturnsCMajor()
        {
            var majorProfile = new double[] { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };

            var (key, confidence) = KeyDetector.DetectKeyFromChromagram(majorProfile, "krumhansl");

            key.Should().Be("C");
            confidence.Should().BeGreaterThan(0.8f);
        }

        [Fact]
        public void DetectKey_TemperleyProfileExplicit_ReturnsCMajor()
        {
            var temperleyMajor = new double[] { 5.0, 2.0, 3.5, 2.0, 4.5, 4.0, 2.0, 4.5, 2.0, 3.5, 1.5, 4.0 };

            var (key, confidence) = KeyDetector.DetectKeyFromChromagram(temperleyMajor, "temperley");

            key.Should().Be("C");
            confidence.Should().BeGreaterThan(0.8f);
        }

        [Fact]
        public void DetectKey_AutoProfileAndUnknownProfile_StillDetectClearTriad()
        {
            var chroma = new double[12];
            chroma[9] = 1.0; // A
            chroma[0] = 0.8; // C
            chroma[4] = 0.6; // E

            var (autoKey, _) = KeyDetector.DetectKeyFromChromagram(chroma, "auto");
            var (unknownKey, _) = KeyDetector.DetectKeyFromChromagram(chroma, "not-a-profile");

            autoKey.Should().Be("Am");
            unknownKey.Should().Be("Am");
        }

        [Theory]
        [InlineData(440.0, 9.0)]
        [InlineData(261.63, 0.0)]
        public void FreqToPitchClassFloat_ReturnsExpectedPitchClass(double freq, double expectedPitchClass)
        {
            double actual = KeyDetector.FreqToPitchClassFloat(freq);
            actual.Should().BeApproximately(expectedPitchClass, 0.05);
        }
    }
}
