using System;
using System.Drawing;
using System.IO;
using Audex.UI;
using FluentAssertions;
using Xunit;

namespace Audex.Tests
{
    public class WaveformCacheTests
    {
        [Fact]
        public void ComputeCacheKey_IsStableAndHexEncoded()
        {
            byte[] inputA = { 1, 2, 3, 4 };
            byte[] inputB = { 1, 2, 3, 5 };

            string keyA1 = WaveformCache.ComputeCacheKey(inputA);
            string keyA2 = WaveformCache.ComputeCacheKey(inputA);
            string keyB = WaveformCache.ComputeCacheKey(inputB);

            keyA1.Should().Be(keyA2);
            keyA1.Should().NotBe(keyB);
            keyA1.Length.Should().Be(64);
            keyA1.Should().MatchRegex("^[0-9a-f]{64}$");
        }

        [Fact]
        public void PeakCache_WriteThenRead_RoundTripsValues()
        {
            string key = "wf-" + Guid.NewGuid().ToString("N");
            float[] peaks = { 0.1f, 0.25f, 0.5f, 0.75f, 1.0f };
            string path = WaveformCache.GetCachePath(key);

            try
            {
                WaveformCache.WriteCache(key, peaks);

                float[]? restored = WaveformCache.ReadCache(key);
                restored.Should().NotBeNull();
                restored.Should().Equal(peaks);
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void PeakCache_Read_WithInvalidCount_ReturnsNull()
        {
            string key = "wf-invalid-" + Guid.NewGuid().ToString("N");
            string path = WaveformCache.GetCachePath(key);

            try
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(fs))
                {
                    writer.Write(-1); // invalid peak count
                }

                WaveformCache.ReadCache(key).Should().BeNull();
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void ColorCache_WriteThenRead_RoundTripsValues()
        {
            string key = "wfc-" + Guid.NewGuid().ToString("N");
            string path = GetColorCachePath(key);
            Color[] colors =
            {
                Color.FromArgb(10, 20, 30),
                Color.FromArgb(40, 50, 60),
                Color.FromArgb(70, 80, 90)
            };

            try
            {
                WaveformCache.WriteColorCache(key, colors);

                Color[]? restored = WaveformCache.ReadColorCache(key);
                restored.Should().NotBeNull();
                restored!.Length.Should().Be(colors.Length);
                for (int i = 0; i < colors.Length; i++)
                {
                    restored[i].ToArgb().Should().Be(colors[i].ToArgb());
                }
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void ColorCache_Read_WithVersionMismatch_ReturnsNullAndDeletesFile()
        {
            string key = "wfc-badver-" + Guid.NewGuid().ToString("N");
            string path = GetColorCachePath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            try
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(fs))
                {
                    writer.Write((byte)99); // unsupported version
                    writer.Write(1);        // count
                    writer.Write((byte)1);
                    writer.Write((byte)2);
                    writer.Write((byte)3);
                }

                WaveformCache.ReadColorCache(key).Should().BeNull();
                File.Exists(path).Should().BeFalse();
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static string GetColorCachePath(string key) =>
            Path.Combine(Path.GetTempPath(), "Audex", key + ".wfc");

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
