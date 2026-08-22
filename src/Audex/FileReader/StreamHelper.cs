using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Audex.FileReader
{
    /// <summary>
    /// Helper methods for reading data from COM IStream objects.
    /// Handles marshaling and memory management for IStream.Read operations.
    /// </summary>
    public static class StreamHelper
    {
        private const int CopyBufferSize = 64 * 1024;

        /// <summary>
        /// Reads exactly the specified number of bytes from an IStream.
        /// Throws if unable to read the full amount.
        /// </summary>
        /// <param name="stream">The IStream to read from</param>
        /// <param name="count">Number of bytes to read</param>
        /// <returns>Byte array containing the read data</returns>
        public static byte[] ReadBytes(IStream stream, int count)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            byte[] buffer = new byte[count];
            IntPtr bytesReadPtr = Marshal.AllocCoTaskMem(sizeof(int));

            try
            {
                Marshal.WriteInt32(bytesReadPtr, 0);
                stream.Read(buffer, count, bytesReadPtr);
                int bytesRead = Marshal.ReadInt32(bytesReadPtr);

                if (bytesRead != count)
                {
                    throw new InvalidOperationException($"Expected {count} bytes, got {bytesRead}");
                }

                return buffer;
            }
            finally
            {
                Marshal.FreeCoTaskMem(bytesReadPtr);
            }
        }

        /// <summary>
        /// Attempts to read bytes from an IStream without throwing.
        /// Returns the actual number of bytes read (may be less than requested).
        /// </summary>
        /// <param name="stream">The IStream to read from</param>
        /// <param name="buffer">Buffer to read into</param>
        /// <param name="count">Number of bytes to attempt to read</param>
        /// <param name="offset">Offset in buffer to start writing (default 0)</param>
        /// <returns>Number of bytes actually read</returns>
        public static int TryReadBytes(IStream stream, byte[] buffer, int count, int offset = 0)
        {
            if (stream == null || buffer == null || count < 0 || offset < 0 ||
                offset > buffer.Length || count > buffer.Length - offset)
            {
                return 0;
            }

            IntPtr bytesReadPtr = Marshal.AllocCoTaskMem(sizeof(int));

            try
            {
                // If offset is specified, create a temporary buffer and copy
                if (offset > 0)
                {
                    byte[] tempBuffer = new byte[count];
                    Marshal.WriteInt32(bytesReadPtr, 0);
                    stream.Read(tempBuffer, count, bytesReadPtr);
                    int bytesRead = Marshal.ReadInt32(bytesReadPtr);
                    if (bytesRead < 0 || bytesRead > count)
                        return 0;

                    // Copy to target buffer at offset
                    Array.Copy(tempBuffer, 0, buffer, offset, bytesRead);
                    return bytesRead;
                }
                else
                {
                    Marshal.WriteInt32(bytesReadPtr, 0);
                    stream.Read(buffer, count, bytesReadPtr);
                    int bytesRead = Marshal.ReadInt32(bytesReadPtr);
                    return bytesRead >= 0 && bytesRead <= count ? bytesRead : 0;
                }
            }
            catch
            {
                return 0;
            }
            finally
            {
                Marshal.FreeCoTaskMem(bytesReadPtr);
            }
        }

        /// <summary>
        /// Reads a COM stream into one bounded byte array. The declared length is used only as
        /// an initial capacity: the actual byte count is authoritative and may not exceed
        /// <paramref name="maxBytes"/>. This avoids MemoryStream.ToArray's second full-size copy.
        /// </summary>
        public static byte[] ReadToEndBounded(
            IStream stream,
            long declaredLength,
            int maxBytes,
            Func<bool>? shouldContinue = null,
            Action? progressPump = null)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            if (declaredLength < 0)
                throw new InvalidDataException("The stream reported a negative length.");
            if (declaredLength > maxBytes)
                throw new InvalidDataException(
                    $"The audio file is too large to preview ({declaredLength:N0} bytes; limit {maxBytes:N0} bytes).");

            stream.Seek(0, 0, IntPtr.Zero); // STREAM_SEEK_SET

            int initialLength = declaredLength > 0
                ? checked((int)declaredLength)
                : Math.Min(CopyBufferSize, maxBytes);
            byte[] result = initialLength == 0 ? Array.Empty<byte>() : new byte[initialLength];
            byte[] copyBuffer = new byte[Math.Min(CopyBufferSize, maxBytes)];
            int totalBytes = 0;
            int chunksSincePump = 0;

            IntPtr bytesReadPtr = Marshal.AllocCoTaskMem(sizeof(int));
            try
            {
                while (shouldContinue == null || shouldContinue())
                {
                    Marshal.WriteInt32(bytesReadPtr, 0);
                    stream.Read(copyBuffer, copyBuffer.Length, bytesReadPtr);
                    int bytesRead = Marshal.ReadInt32(bytesReadPtr);

                    if (bytesRead == 0)
                        break;
                    if (bytesRead < 0 || bytesRead > copyBuffer.Length)
                        throw new InvalidDataException($"The stream returned an invalid byte count: {bytesRead}.");

                    int requiredLength = checked(totalBytes + bytesRead);
                    if (requiredLength > maxBytes)
                        throw new InvalidDataException(
                            $"The audio file exceeds the preview size limit of {maxBytes:N0} bytes.");

                    EnsureCapacity(ref result, requiredLength, maxBytes);
                    Buffer.BlockCopy(copyBuffer, 0, result, totalBytes, bytesRead);
                    totalBytes = requiredLength;

                    if (++chunksSincePump >= 8)
                    {
                        chunksSincePump = 0;
                        progressPump?.Invoke();
                    }
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(bytesReadPtr);
            }

            if (totalBytes == 0)
                return Array.Empty<byte>();
            if (totalBytes != result.Length)
                Array.Resize(ref result, totalBytes);
            return result;
        }

        private static void EnsureCapacity(ref byte[] buffer, int requiredLength, int maxBytes)
        {
            if (requiredLength <= buffer.Length)
                return;

            int doubled = buffer.Length == 0
                ? Math.Min(CopyBufferSize, maxBytes)
                : buffer.Length > maxBytes / 2 ? maxBytes : buffer.Length * 2;
            int newLength = Math.Max(requiredLength, doubled);
            if (newLength > maxBytes)
                newLength = maxBytes;
            Array.Resize(ref buffer, newLength);
        }
    }
}
