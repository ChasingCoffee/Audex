using System;
using Audex.FileReader;
using FluentAssertions;
using Xunit;

namespace Audex.Tests
{
    public class StreamHelperTests
    {
        [Fact]
        public void ReadBytes_ReadsRequestedCount()
        {
            using var stream = new TestComStream(new byte[] { 1, 2, 3, 4, 5 });

            byte[] data = StreamHelper.ReadBytes(stream, 3);

            data.Should().Equal(new byte[] { 1, 2, 3 });
        }

        [Fact]
        public void ReadBytes_WhenShortRead_Throws()
        {
            using var stream = new TestComStream(new byte[] { 1, 2 });

            Action act = () => StreamHelper.ReadBytes(stream, 4);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("Expected 4 bytes, got 2");
        }

        [Fact]
        public void TryReadBytes_ReturnsActualCount()
        {
            using var stream = new TestComStream(new byte[] { 10, 20 });
            byte[] buffer = new byte[4];

            int read = StreamHelper.TryReadBytes(stream, buffer, 4);

            read.Should().Be(2);
            buffer[0].Should().Be(10);
            buffer[1].Should().Be(20);
        }

        [Fact]
        public void TryReadBytes_WithOffset_WritesAtOffset()
        {
            using var stream = new TestComStream(new byte[] { 9, 8, 7 });
            byte[] buffer = new byte[] { 1, 1, 1, 1, 1, 1 };

            int read = StreamHelper.TryReadBytes(stream, buffer, 2, offset: 3);

            read.Should().Be(2);
            buffer.Should().Equal(new byte[] { 1, 1, 1, 9, 8, 1 });
        }

        [Fact]
        public void TryReadBytes_WhenReadThrows_ReturnsZero()
        {
            using var stream = new TestComStream(new byte[] { 1, 2, 3 }, throwOnRead: true);
            byte[] buffer = new byte[4];

            int read = StreamHelper.TryReadBytes(stream, buffer, 4);

            read.Should().Be(0);
        }
    }
}
