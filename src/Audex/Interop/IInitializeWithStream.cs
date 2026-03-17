using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Audex.Interop
{
    /// <summary>
    /// IInitializeWithStream interface - Used to initialize a preview handler with a stream.
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
    public interface IInitializeWithStream
    {
        /// <summary>
        /// Initializes the handler with a stream.
        /// </summary>
        /// <param name="pstream">The stream containing the file data.</param>
        /// <param name="grfMode">The access mode (read/write) for the stream.</param>
        void Initialize(IStream pstream, uint grfMode);
    }
}
