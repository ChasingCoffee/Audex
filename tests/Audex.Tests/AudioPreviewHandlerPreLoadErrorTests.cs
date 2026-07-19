using Audex.PreviewHandler;
using FluentAssertions;
using Xunit;

namespace Audex.Tests
{
    public class AudioPreviewHandlerPreLoadErrorTests
    {
        [Fact]
        public void FormatUnsupported_ReturnsFormatReason_EvenWhenDeviceUnavailable()
        {
            string? error = AudioPreviewHandler.ResolvePreLoadError(
                formatSupported: false,
                unsupportedFormatReason: "WMA plugin not found",
                wasapiReady: false);

            error.Should().Be("WMA plugin not found");
        }

        [Fact]
        public void FormatUnsupported_ReturnsFormatReason_WhenDeviceIsAvailable()
        {
            string? error = AudioPreviewHandler.ResolvePreLoadError(
                formatSupported: false,
                unsupportedFormatReason: "OPUS plugin not found",
                wasapiReady: true);

            error.Should().Be("OPUS plugin not found");
        }

        [Fact]
        public void FormatSupported_ButWasapiNotReady_ReturnsDeviceUnavailableMessage()
        {
            string? error = AudioPreviewHandler.ResolvePreLoadError(
                formatSupported: true,
                unsupportedFormatReason: null,
                wasapiReady: false);

            error.Should().Be("Audio output device unavailable. Check your Windows sound settings.");
        }

        [Fact]
        public void FormatSupported_AndWasapiReady_ReturnsNull()
        {
            string? error = AudioPreviewHandler.ResolvePreLoadError(
                formatSupported: true,
                unsupportedFormatReason: null,
                wasapiReady: true);

            error.Should().BeNull();
        }
    }
}
