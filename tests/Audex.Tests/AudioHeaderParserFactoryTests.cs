using Audex.FileReader;
using FluentAssertions;
using Xunit;

namespace Audex.Tests
{
    public class AudioHeaderParserFactoryTests
    {
        [Fact]
        public void Parse_UnknownExtension_ReturnsUnsupportedBasicInfo()
        {
            using var stream = new TestComStream(new byte[16]);

            AudioFileInfo info = AudioHeaderParserFactory.Parse(stream, "track.xyz", fileSize: 1234);

            info.ParseSucceeded.Should().BeTrue();
            info.Format.Should().Be("XYZ");
            info.FileName.Should().Be("track.xyz");
            info.FileSize.Should().Be(1234);
            info.SampleRate.Should().Be(0);
            info.Duration.Should().Be(0);
        }

        [Fact]
        public void Parse_NoExtension_WithOggMagic_DetectsOggAndReturnsUnsupportedBasicInfo()
        {
            byte[] data = new byte[16];
            data[0] = (byte)'O';
            data[1] = (byte)'g';
            data[2] = (byte)'g';
            data[3] = (byte)'S';
            using var stream = new TestComStream(data);

            AudioFileInfo info = AudioHeaderParserFactory.Parse(stream, "no_extension_name", fileSize: data.Length);

            info.ParseSucceeded.Should().BeTrue();
            info.Format.Should().Be("OGG");
            info.SampleRate.Should().Be(0);
            info.BitDepth.Should().Be(0);
        }

        [Fact]
        public void Parse_NoExtension_WithUnknownMagic_FallsBackToGenericAudioFormat()
        {
            using var stream = new TestComStream(new byte[16]);

            AudioFileInfo info = AudioHeaderParserFactory.Parse(stream, "unknownfile", fileSize: 16);

            info.ParseSucceeded.Should().BeTrue();
            info.Format.Should().Be("Audio");
        }

        [Fact]
        public void Parse_WavExtensionWithInvalidData_ReturnsParseFailure()
        {
            using var stream = new TestComStream(new byte[] { 0x00, 0x01, 0x02, 0x03 });

            AudioFileInfo info = AudioHeaderParserFactory.Parse(stream, "bad.wav", fileSize: 4);

            info.ParseSucceeded.Should().BeFalse();
            info.Format.Should().Be("WAV");
            info.ParseError.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void Parse_AacExtension_ReturnsUnsupportedAacBasicInfo()
        {
            using var stream = new TestComStream(new byte[16]);

            AudioFileInfo info = AudioHeaderParserFactory.Parse(stream, "song.aac", fileSize: 2048);

            info.ParseSucceeded.Should().BeTrue();
            info.Format.Should().Be("AAC");
            info.FileSize.Should().Be(2048);
        }
    }
}
