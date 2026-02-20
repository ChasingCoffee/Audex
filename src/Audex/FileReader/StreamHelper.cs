using System;
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
        /// <summary>
        /// Reads exactly the specified number of bytes from an IStream.
        /// Throws if unable to read the full amount.
        /// </summary>
        /// <param name="stream">The IStream to read from</param>
        /// <param name="count">Number of bytes to read</param>
        /// <returns>Byte array containing the read data</returns>
        public static byte[] ReadBytes(IStream stream, int count)
        {
            byte[] buffer = new byte[count];
            IntPtr bytesReadPtr = Marshal.AllocCoTaskMem(sizeof(int));

            try
            {
                stream.Read(buffer, count, bytesReadPtr);
                int bytesRead = Marshal.ReadInt32(bytesReadPtr);

                if (bytesRead < count)
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
            IntPtr bytesReadPtr = Marshal.AllocCoTaskMem(sizeof(int));

            try
            {
                // If offset is specified, create a temporary buffer and copy
                if (offset > 0)
                {
                    byte[] tempBuffer = new byte[count];
                    stream.Read(tempBuffer, count, bytesReadPtr);
                    int bytesRead = Marshal.ReadInt32(bytesReadPtr);

                    // Copy to target buffer at offset
                    Array.Copy(tempBuffer, 0, buffer, offset, bytesRead);
                    return bytesRead;
                }
                else
                {
                    stream.Read(buffer, count, bytesReadPtr);
                    return Marshal.ReadInt32(bytesReadPtr);
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
    }
}
