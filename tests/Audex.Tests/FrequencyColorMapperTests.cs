using System;
using System.Drawing;
using Audex.Audio;
using FluentAssertions;
using Xunit;

namespace Audex.Tests
{
    public class FrequencyColorMapperTests
    {
        private const int SampleRate = 44100;
        private const int FftSize = 2048;

        // ── FreqToBin ──

        [Fact]
        public void FreqToBin_440Hz_At44100_2048()
        {
            // bin = (int)(440 * 2048 / 44100) = (int)(20.41...) = 20
            FrequencyColorMapper.FreqToBin(440f, 44100, 2048).Should().Be(20);
        }

        [Fact]
        public void FreqToBin_0Hz_Returns0()
        {
            FrequencyColorMapper.FreqToBin(0f, 44100, 2048).Should().Be(0);
        }

        [Theory]
        [InlineData(44100, 2048)]
        [InlineData(48000, 2048)]
        [InlineData(22050, 2048)]
        public void FreqToBin_ScalesWithSampleRate(int sr, int fft)
        {
            int bin = FrequencyColorMapper.FreqToBin(1000f, sr, fft);
            // bin = (int)(1000 * fft / sr)
            int expected = (int)(1000f * fft / sr);
            bin.Should().Be(expected);
        }

        // ── BandRms ──

        [Fact]
        public void BandRms_UniformEnergy_ReturnsExpectedValue()
        {
            // 10 bins all at 0.5 => RMS = sqrt(sum(0.25)/10) = sqrt(0.025) ≈ 0.158
            var fft = new float[20];
            for (int i = 0; i < fft.Length; i++)
                fft[i] = 0.5f;

            float rms = FrequencyColorMapper.BandRms(fft, 5, 15);
            rms.Should().BeApproximately(0.5f, 0.001f); // RMS of constant = that constant
        }

        [Fact]
        public void BandRms_AllZeros_ReturnsZero()
        {
            var fft = new float[100];
            FrequencyColorMapper.BandRms(fft, 10, 50).Should().Be(0f);
        }

        [Fact]
        public void BandRms_NullFft_ReturnsZero()
        {
            FrequencyColorMapper.BandRms(null!, 0, 10).Should().Be(0f);
        }

        [Fact]
        public void BandRms_StartGreaterThanEnd_ReturnsZero()
        {
            var fft = new float[100];
            FrequencyColorMapper.BandRms(fft, 50, 10).Should().Be(0f);
        }

        [Fact]
        public void BandRms_SkipsDCBin()
        {
            // If start=0, BandRms should clamp to start=1
            var fft = new float[10];
            fft[0] = 100f; // DC bin — should be skipped
            fft[1] = 1f;
            fft[2] = 1f;

            float rms = FrequencyColorMapper.BandRms(fft, 0, 3);
            // Should only use bins 1 and 2, both at 1.0
            // RMS = sqrt((1+1)/2) = 1.0
            rms.Should().BeApproximately(1f, 0.001f);
        }

        // ── NeutralColor ──

        [Fact]
        public void NeutralColor_Dark_Returns60_60_65()
        {
            var c = FrequencyColorMapper.NeutralColor(true);
            c.R.Should().Be(60);
            c.G.Should().Be(60);
            c.B.Should().Be(65);
        }

        [Fact]
        public void NeutralColor_Light_Returns180_180_185()
        {
            var c = FrequencyColorMapper.NeutralColor(false);
            c.R.Should().Be(180);
            c.G.Should().Be(180);
            c.B.Should().Be(185);
        }

        // ── Compute ──

        [Fact]
        public void Compute_NullFft_ReturnsNeutralColor()
        {
            var result = FrequencyColorMapper.Compute(null!, SampleRate, FftSize, true);
            result.Should().Be(FrequencyColorMapper.NeutralColor(true));
        }

        [Fact]
        public void Compute_EmptyFft_ReturnsNeutralColor()
        {
            var result = FrequencyColorMapper.Compute(new float[0], SampleRate, FftSize, false);
            result.Should().Be(FrequencyColorMapper.NeutralColor(false));
        }

        [Fact]
        public void Compute_ZeroSampleRate_ReturnsNeutralColor()
        {
            var fft = new float[1024];
            fft[10] = 1f;
            var result = FrequencyColorMapper.Compute(fft, 0, FftSize, true);
            result.Should().Be(FrequencyColorMapper.NeutralColor(true));
        }

        [Fact]
        public void Compute_SilentFft_ReturnsNeutralColor()
        {
            var fft = new float[1024]; // all zeros
            var result = FrequencyColorMapper.Compute(fft, SampleRate, FftSize, true);
            result.Should().Be(FrequencyColorMapper.NeutralColor(true));
        }

        [Fact]
        public void Compute_BassHeavy_RedDominant()
        {
            // Energy concentrated in bass band (bins for 20-200 Hz)
            var fft = new float[1024];
            int bassStart = FrequencyColorMapper.FreqToBin(20f, SampleRate, FftSize);
            int bassEnd = FrequencyColorMapper.FreqToBin(200f, SampleRate, FftSize);
            for (int i = Math.Max(1, bassStart); i < bassEnd && i < fft.Length; i++)
                fft[i] = 1.0f;

            var color = FrequencyColorMapper.Compute(fft, SampleRate, FftSize, true);

            // R=bass should dominate
            color.R.Should().BeGreaterThan(color.G);
            color.R.Should().BeGreaterThan(color.B);
        }

        [Fact]
        public void Compute_MidsHeavy_GreenDominant()
        {
            // Energy concentrated in mids band (bins for 200-1500 Hz)
            var fft = new float[1024];
            int midsStart = FrequencyColorMapper.FreqToBin(200f, SampleRate, FftSize);
            int midsEnd = FrequencyColorMapper.FreqToBin(1500f, SampleRate, FftSize);
            for (int i = midsStart; i < midsEnd && i < fft.Length; i++)
                fft[i] = 1.0f;

            var color = FrequencyColorMapper.Compute(fft, SampleRate, FftSize, true);

            // G=mids should dominate
            color.G.Should().BeGreaterThan(color.R);
            color.G.Should().BeGreaterThan(color.B);
        }

        [Fact]
        public void Compute_HighsHeavy_BlueDominant()
        {
            // Energy concentrated in highs band (bins for 1500-16000 Hz)
            var fft = new float[1024];
            int highStart = FrequencyColorMapper.FreqToBin(1500f, SampleRate, FftSize);
            int highEnd = FrequencyColorMapper.FreqToBin(16000f, SampleRate, FftSize);
            for (int i = highStart; i < highEnd && i < fft.Length; i++)
                fft[i] = 1.0f;

            var color = FrequencyColorMapper.Compute(fft, SampleRate, FftSize, true);

            // B=highs should dominate
            color.B.Should().BeGreaterThan(color.R);
            color.B.Should().BeGreaterThan(color.G);
        }

        [Fact]
        public void Compute_DarkMode_HigherCeiling()
        {
            // Same FFT data, dark mode should produce brighter (higher ceiling=200 vs 170)
            var fft = new float[1024];
            int midsStart = FrequencyColorMapper.FreqToBin(200f, SampleRate, FftSize);
            int midsEnd = FrequencyColorMapper.FreqToBin(1500f, SampleRate, FftSize);
            for (int i = midsStart; i < midsEnd && i < fft.Length; i++)
                fft[i] = 1.0f;

            var dark = FrequencyColorMapper.Compute(fft, SampleRate, FftSize, isDark: true);
            var light = FrequencyColorMapper.Compute(fft, SampleRate, FftSize, isDark: false);

            // Dominant channel should be brighter in dark mode
            Math.Max(dark.R, Math.Max(dark.G, dark.B))
                .Should().BeGreaterThan(Math.Max(light.R, Math.Max(light.G, light.B)));
        }

        [Fact]
        public void Compute_RGBValuesClampedTo255()
        {
            // Even with extreme FFT values, RGB should never exceed 255
            var fft = new float[1024];
            for (int i = 1; i < fft.Length; i++)
                fft[i] = 100f; // very high values

            var color = FrequencyColorMapper.Compute(fft, SampleRate, FftSize, true);

            color.R.Should().BeLessThanOrEqualTo(255);
            color.G.Should().BeLessThanOrEqualTo(255);
            color.B.Should().BeLessThanOrEqualTo(255);
        }

        // ── SmoothColors ──

        [Fact]
        public void SmoothColors_NullInput_NoException()
        {
            var act = () => FrequencyColorMapper.SmoothColors(null!);
            act.Should().NotThrow();
        }

        [Fact]
        public void SmoothColors_TwoElements_NoChange()
        {
            var colors = new[]
            {
                Color.FromArgb(100, 0, 0),
                Color.FromArgb(0, 100, 0)
            };
            var before = (Color[])colors.Clone();

            FrequencyColorMapper.SmoothColors(colors);

            colors[0].ToArgb().Should().Be(before[0].ToArgb());
            colors[1].ToArgb().Should().Be(before[1].ToArgb());
        }

        [Fact]
        public void SmoothColors_ThreeElements_AveragesMiddle()
        {
            var colors = new[]
            {
                Color.FromArgb(30, 60, 90),
                Color.FromArgb(60, 120, 150),
                Color.FromArgb(90, 180, 210)
            };

            FrequencyColorMapper.SmoothColors(colors);

            // Middle element = average of all three
            // R: (30+60+90)/3 = 60, G: (60+120+180)/3 = 120, B: (90+150+210)/3 = 150
            colors[1].R.Should().Be(60);
            colors[1].G.Should().Be(120);
            colors[1].B.Should().Be(150);

            // First and last should be unchanged
            colors[0].R.Should().Be(30);
            colors[2].R.Should().Be(90);
        }

        [Fact]
        public void SmoothColors_PreservesFirstAndLast()
        {
            var colors = new[]
            {
                Color.FromArgb(255, 0, 0),
                Color.FromArgb(0, 255, 0),
                Color.FromArgb(0, 0, 255),
                Color.FromArgb(128, 128, 128)
            };
            var first = colors[0];
            var last = colors[colors.Length - 1];

            FrequencyColorMapper.SmoothColors(colors);

            colors[0].ToArgb().Should().Be(first.ToArgb());
            colors[colors.Length - 1].ToArgb().Should().Be(last.ToArgb());
        }

        [Fact]
        public void SmoothColors_NoCascadingArtifacts()
        {
            // With in-place averaging, earlier averaged values would affect later ones.
            // The implementation uses a temp array, so this shouldn't happen.
            var colors = new[]
            {
                Color.FromArgb(0, 0, 0),
                Color.FromArgb(255, 255, 255),
                Color.FromArgb(0, 0, 0),
                Color.FromArgb(255, 255, 255),
                Color.FromArgb(0, 0, 0)
            };

            FrequencyColorMapper.SmoothColors(colors);

            // Index 1: avg(0, 255, 0) = 85
            colors[1].R.Should().Be(85);
            // Index 3: avg(0, 255, 0) = 85 — NOT affected by smoothed index 2
            colors[3].R.Should().Be(85);
        }
    }
}
