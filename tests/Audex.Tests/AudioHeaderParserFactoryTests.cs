using System;
using System.Text;
using System.Threading.Tasks;
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

        [Fact]
        public async Task Parse_WavWithOverflowingChunkSize_ReturnsWithoutLooping()
        {
            byte[] data = new byte[20];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(data, 0);
            BitConverter.GetBytes(12u).CopyTo(data, 4);
            Encoding.ASCII.GetBytes("WAVE").CopyTo(data, 8);
            Encoding.ASCII.GetBytes("JUNK").CopyTo(data, 12);
            BitConverter.GetBytes(0xfffffff8u).CopyTo(data, 16);

            Task<AudioFileInfo> parseTask = Task.Run(() =>
            {
                using var stream = new TestComStream(data);
                return AudioHeaderParserFactory.Parse(stream, "crafted.wav", data.Length);
            });

            Task completedTask = await Task.WhenAny(parseTask, Task.Delay(TimeSpan.FromSeconds(1)));
            completedTask.Should().BeSameAs(parseTask,
                "a malformed RIFF chunk must never send the preview STA into an infinite seek loop");
            AudioFileInfo info = await parseTask;
            info.ParseSucceeded.Should().BeFalse();
        }
    }
}
