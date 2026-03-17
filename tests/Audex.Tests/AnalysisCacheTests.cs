using System;
using System.IO;
using Audex.Audio;
using FluentAssertions;
using Xunit;

namespace Audex.Tests
{
    public class AnalysisCacheTests
    {
        [Fact]
        public void WriteThenRead_RoundTripsDetectedValues()
        {
            string key = "bka-" + Guid.NewGuid().ToString("N");
            string path = GetAnalysisPath(key);
            var result = new AnalysisResult
            {
                DetectedBpm = 128,
                DetectedKey = "Am",
                BpmConfidence = 0.92f,
                KeyConfidence = 0.81f,
                BpmFailed = false,
                KeyFailed = false
            };

            try
            {
                AnalysisCache.Write(key, result);
                AnalysisResult? restored = AnalysisCache.Read(key);

                restored.Should().NotBeNull();
                restored!.DetectedBpm.Should().Be(128);
                restored.DetectedKey.Should().Be("Am");
                restored.BpmConfidence.Should().BeApproximately(0.92f, 0.0001f);
                restored.KeyConfidence.Should().BeApproximately(0.81f, 0.0001f);
                restored.BpmFailed.Should().BeFalse();
                restored.KeyFailed.Should().BeFalse();
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void WriteThenRead_WhenBothDetectionsFail_PreservesFailureFlags()
        {
            string key = "bka-fail-" + Guid.NewGuid().ToString("N");
            string path = GetAnalysisPath(key);
            var result = new AnalysisResult
            {
                DetectedBpm = null,
                DetectedKey = null,
                BpmConfidence = 0f,
                KeyConfidence = 0f,
                BpmFailed = true,
                KeyFailed = true
            };

            try
            {
                AnalysisCache.Write(key, result);
                AnalysisResult? restored = AnalysisCache.Read(key);

                restored.Should().NotBeNull();
                restored!.DetectedBpm.Should().BeNull();
                restored.DetectedKey.Should().BeNull();
                restored.BpmFailed.Should().BeTrue();
                restored.KeyFailed.Should().BeTrue();
                restored.FailureReason.Should().Be("unable to detect");
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Write_WithVeryLongKey_TruncatesToTenCharactersOnRead()
        {
            string key = "bka-longkey-" + Guid.NewGuid().ToString("N");
            string path = GetAnalysisPath(key);
            string longKey = new string('A', 300);
            var result = new AnalysisResult
            {
                DetectedBpm = 120,
                DetectedKey = longKey,
                BpmConfidence = 0.5f,
                KeyConfidence = 0.5f
            };

            try
            {
                AnalysisCache.Write(key, result);
                AnalysisResult? restored = AnalysisCache.Read(key);

                restored.Should().NotBeNull();
                restored!.DetectedKey.Should().Be("AAAAAAAAAA");
            }
            finally
            {
                TryDelete(path);
            }
        }

        [Fact]
        public void Delete_RemovesExistingEntry()
        {
            string key = "bka-delete-" + Guid.NewGuid().ToString("N");
            string path = GetAnalysisPath(key);
            var result = new AnalysisResult { DetectedBpm = 100, DetectedKey = "C" };

            AnalysisCache.Write(key, result);
            File.Exists(path).Should().BeTrue();

            AnalysisCache.Delete(key);

            File.Exists(path).Should().BeFalse();
        }

        [Fact]
        public void Read_MissingEntry_ReturnsNull()
        {
            string key = "bka-missing-" + Guid.NewGuid().ToString("N");
            AnalysisCache.Read(key).Should().BeNull();
        }

        [Fact]
        public void Read_WithInvalidVersion_ReturnsNullAndDeletesEntry()
        {
            string key = "bka-badver-" + Guid.NewGuid().ToString("N");
            string path = GetAnalysisPath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            try
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(fs))
                {
                    writer.Write((byte)99); // invalid cache version
                }

                AnalysisCache.Read(key).Should().BeNull();
                File.Exists(path).Should().BeFalse();
            }
            finally
            {
                TryDelete(path);
            }
        }

        private static string GetAnalysisPath(string key) =>
            Path.Combine(Path.GetTempPath(), "Audex", "analysis", key + ".bka");

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
