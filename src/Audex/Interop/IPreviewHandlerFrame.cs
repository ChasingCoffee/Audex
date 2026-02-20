using System;
using System.Runtime.InteropServices;

namespace Audex.Interop
{
    /// <summary>
    /// IPreviewHandlerFrame interface - Enables preview handlers to communicate with the host.
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("fec87aaf-35f9-447a-adb7-20234fb69178")]
    public interface IPreviewHandlerFrame
    {
        /// <summary>
        /// Gets the window context information.
        /// </summary>
        void GetWindowContext(out IntPtr pinfo);

        /// <summary>
        /// Handles keyboard accelerators.
        /// </summary>
        [PreserveSig]
        uint TranslateAccelerator(ref MSG pmsg);
    }
}
