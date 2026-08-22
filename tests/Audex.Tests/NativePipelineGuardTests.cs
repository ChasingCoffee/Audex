using System;
using System.Threading;
using Audex.Audio;
using FluentAssertions;
using Xunit;

namespace Audex.Tests
{
    public class NativePipelineGuardTests
    {
        [Fact]
        public void BpmAnalysis_WhenAlreadyCancelled_DoesNotEnterNativePipeline()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            AnalysisResult? result = BpmKeyAnalyzer.Analyze(
                new byte[] { 1, 2, 3 }, cts.Token, _ => { });

            result.Should().BeNull();
        }

        [Fact]
        public void WaveformGeneration_WhenAlreadyCancelled_DoesNotEnterNativePipeline()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            WaveformData? result = WaveformGenerator.Generate(
                new byte[] { 1, 2, 3 }, cts.Token);

            result.Should().BeNull();
        }

        [Fact]
        public void WaveformGeneration_WithInvalidAudio_FailsCleanlyInsideNativePipeline()
        {
            WaveformData? result = WaveformGenerator.Generate(
                new byte[] { 1, 2, 3 }, CancellationToken.None);

            result.Should().BeNull();
        }

        [Fact]
        public void BpmAnalysis_WithInvalidAudio_ReturnsStructuredFailure()
        {
            AnalysisResult? result = BpmKeyAnalyzer.Analyze(
                new byte[] { 1, 2, 3 }, CancellationToken.None, _ => { });

            result.Should().NotBeNull();
            result!.BpmFailed.Should().BeTrue();
            result.KeyFailed.Should().BeTrue();
            result.FailureReason.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public void AudioPlayer_LoadBeforeInitialize_ThrowsClearError()
        {
            using var player = new AudioPlayer();

            Action act = () => player.LoadFile(new byte[] { 1, 2, 3 }, "test.wav");

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*not initialized*");
        }

        [Fact]
        public void TagReader_InvalidData_ReturnsEmptyMetadata()
        {
            TagInfo tags = TagReader.ReadTags(new byte[] { 1, 2, 3 }, "invalid.mp3");

            tags.Title.Should().BeNull();
            tags.Artist.Should().BeNull();
            tags.Album.Should().BeNull();
        }
    }
}
