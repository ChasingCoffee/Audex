using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Audex.FileReader
{
    /// <summary>
    /// Minimal read-only IStream adapter over an in-memory byte array.
    /// Lets header parsers run against already-buffered file data instead of re-reading the
    /// shell's IStream a second time. Only Seek/Read/Stat are implemented — the only members
    /// the header parsers (via StreamHelper) actually use.
    /// </summary>
    internal sealed class InMemoryComStream : IStream
    {
        private readonly MemoryStream _stream;

        public InMemoryComStream(byte[] data)
        {
            _stream = new MemoryStream(data, writable: false);
        }

        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            int read = _stream.Read(pv, 0, cb);
            if (pcbRead != IntPtr.Zero)
                Marshal.WriteInt32(pcbRead, read);
        }

        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            SeekOrigin origin = dwOrigin switch
            {
                1 => SeekOrigin.Current, // STREAM_SEEK_CUR
                2 => SeekOrigin.End,     // STREAM_SEEK_END
                _ => SeekOrigin.Begin    // STREAM_SEEK_SET
            };
            long newPos = _stream.Seek(dlibMove, origin);
            if (plibNewPosition != IntPtr.Zero)
                Marshal.WriteInt64(plibNewPosition, newPos);
        }

        public void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG pstatstg, int grfStatFlag)
        {
            pstatstg = new System.Runtime.InteropServices.ComTypes.STATSTG { cbSize = _stream.Length, type = 2 /* STGTY_STREAM */ };
        }

        public void Clone(out IStream ppstm) => throw new NotSupportedException();
        public void Commit(int grfCommitFlags) => throw new NotSupportedException();
        public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) => throw new NotSupportedException();
        public void LockRegion(long libOffset, long cb, int dwLockType) => throw new NotSupportedException();
        public void Revert() => throw new NotSupportedException();
        public void SetSize(long libNewSize) => throw new NotSupportedException();
        public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new NotSupportedException();
        public void Write(byte[] pv, int cb, IntPtr pcbWritten) => throw new NotSupportedException();
    }
}
