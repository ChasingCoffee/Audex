using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Audex.Tests
{
    internal sealed class TestComStream : IStream, IDisposable
    {
        private readonly Stream _stream;
        private readonly bool _throwOnRead;

        public TestComStream(byte[] data, bool throwOnRead = false)
        {
            _stream = new MemoryStream(data ?? Array.Empty<byte>(), writable: false);
            _throwOnRead = throwOnRead;
        }

        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            if (_throwOnRead)
                throw new InvalidOperationException("Simulated read failure");

            int read = _stream.Read(pv, 0, cb);
            if (pcbRead != IntPtr.Zero)
                Marshal.WriteInt32(pcbRead, read);
        }

        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            long newPos = _stream.Seek(dlibMove, (SeekOrigin)dwOrigin);
            if (plibNewPosition != IntPtr.Zero)
                Marshal.WriteInt64(plibNewPosition, newPos);
        }

        public void SetSize(long libNewSize) => _stream.SetLength(libNewSize);

        public void Write(byte[] pv, int cb, IntPtr pcbWritten) =>
            throw new NotSupportedException();

        public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) =>
            throw new NotSupportedException();

        public void Commit(int grfCommitFlags)
        {
            // No-op for read-only MemoryStream in tests.
        }

        public void Revert() => throw new NotSupportedException();

        public void LockRegion(long libOffset, long cb, int dwLockType) =>
            throw new NotSupportedException();

        public void UnlockRegion(long libOffset, long cb, int dwLockType) =>
            throw new NotSupportedException();

        public void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG pstatstg, int grfStatFlag)
        {
            pstatstg = new System.Runtime.InteropServices.ComTypes.STATSTG
            {
                cbSize = _stream.Length,
                type = 2 // STGTY_STREAM
            };
        }

        public void Clone(out IStream ppstm) => throw new NotSupportedException();

        public void Dispose()
        {
            _stream.Dispose();
        }
    }
}
