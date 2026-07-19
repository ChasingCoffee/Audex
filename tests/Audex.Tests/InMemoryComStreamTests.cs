using System;
using System.Linq;
using System.Runtime.InteropServices;
using Audex.FileReader;
using FluentAssertions;
using Xunit;

namespace Audex.Tests
{
    public class InMemoryComStreamTests
    {
        [Fact]
        public void Read_ReturnsAllBytes_WhenBufferLargerThanData()
        {
            var stream = new InMemoryComStream(new byte[] { 1, 2, 3, 4, 5 });
            byte[] buffer = new byte[10];

            int read = ReadAll(stream, buffer, 10);

            read.Should().Be(5);
            buffer.Take(5).Should().Equal(new byte[] { 1, 2, 3, 4, 5 });
        }

        [Fact]
        public void Read_AcrossMultipleCalls_ReturnsSequentialChunks()
        {
            var stream = new InMemoryComStream(new byte[] { 1, 2, 3, 4, 5 });
            byte[] buffer = new byte[3];

            int firstRead = ReadAll(stream, buffer, 3);
            firstRead.Should().Be(3);
            buffer.Should().Equal(new byte[] { 1, 2, 3 });

            int secondRead = ReadAll(stream, buffer, 3);
            secondRead.Should().Be(2);
            buffer.Take(2).Should().Equal(new byte[] { 4, 5 });
        }

        [Fact]
        public void Read_AtEndOfStream_ReturnsZero()
        {
            var stream = new InMemoryComStream(new byte[] { 1, 2 });
            byte[] buffer = new byte[4];

            ReadAll(stream, buffer, 4); // consume both bytes
            int read = ReadAll(stream, buffer, 4);

            read.Should().Be(0);
        }

        [Fact]
        public void Seek_FromBegin_PositionsAbsolute()
        {
            var stream = new InMemoryComStream(new byte[] { 10, 20, 30, 40 });
            byte[] buffer = new byte[1];

            Seek(stream, 2, origin: 0); // STREAM_SEEK_SET
            ReadAll(stream, buffer, 1);

            buffer[0].Should().Be(30);
        }

        [Fact]
        public void Seek_FromCurrent_AdvancesRelativeToPosition()
        {
            var stream = new InMemoryComStream(new byte[] { 10, 20, 30, 40 });
            byte[] buffer = new byte[1];

            ReadAll(stream, buffer, 1); // position now at 1
            Seek(stream, 1, origin: 1); // STREAM_SEEK_CUR -> position 2
            ReadAll(stream, buffer, 1);

            buffer[0].Should().Be(30);
        }

        [Fact]
        public void Seek_FromEnd_PositionsRelativeToLength()
        {
            var stream = new InMemoryComStream(new byte[] { 10, 20, 30, 40 });
            byte[] buffer = new byte[1];

            Seek(stream, -1, origin: 2); // STREAM_SEEK_END -> last byte
            ReadAll(stream, buffer, 1);

            buffer[0].Should().Be(40);
        }

        [Fact]
        public void Stat_ReturnsDataLengthAsCbSize()
        {
            var stream = new InMemoryComStream(new byte[] { 1, 2, 3, 4, 5, 6 });

            stream.Stat(out System.Runtime.InteropServices.ComTypes.STATSTG stat, grfStatFlag: 1);

            stat.cbSize.Should().Be(6);
        }

        [Fact]
        public void UnsupportedMembers_ThrowNotSupported()
        {
            var stream = new InMemoryComStream(Array.Empty<byte>());

            Action write = () => stream.Write(Array.Empty<byte>(), 0, IntPtr.Zero);
            Action clone = () => stream.Clone(out _);

            write.Should().Throw<NotSupportedException>();
            clone.Should().Throw<NotSupportedException>();
        }

        [Fact]
        public void Parse_ViaAudioHeaderParserFactory_BehavesSameAsTestComStream()
        {
            // Confirms InMemoryComStream is a drop-in replacement for the shell IStream
            // in the actual production call path (AudioPreviewHandler now feeds it the
            // already-buffered file bytes instead of re-reading the COM stream).
            byte[] invalidWav = { 0x00, 0x01, 0x02, 0x03 };

            using var comStream = new TestComStream(invalidWav);
            var memoryStream = new InMemoryComStream(invalidWav);

            var viaComStream = AudioHeaderParserFactory.Parse(comStream, "bad.wav", invalidWav.Length);
            var viaMemoryStream = AudioHeaderParserFactory.Parse(memoryStream, "bad.wav", invalidWav.Length);

            viaMemoryStream.ParseSucceeded.Should().Be(viaComStream.ParseSucceeded);
            viaMemoryStream.Format.Should().Be(viaComStream.Format);
        }

        private static int ReadAll(InMemoryComStream stream, byte[] buffer, int count)
        {
            IntPtr bytesReadPtr = Marshal.AllocCoTaskMem(sizeof(int));
            try
            {
                stream.Read(buffer, count, bytesReadPtr);
                return Marshal.ReadInt32(bytesReadPtr);
            }
            finally
            {
                Marshal.FreeCoTaskMem(bytesReadPtr);
            }
        }

        private static void Seek(InMemoryComStream stream, long offset, int origin)
        {
            IntPtr posPtr = Marshal.AllocCoTaskMem(sizeof(long));
            try
            {
                stream.Seek(offset, origin, posPtr);
            }
            finally
            {
                Marshal.FreeCoTaskMem(posPtr);
            }
        }
    }
}
