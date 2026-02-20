using System;
using System.Runtime.InteropServices;

namespace Audex.Interop
{
    /// <summary>
    /// IOleWindow interface - Provides methods for manipulating a window.
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("00000114-0000-0000-C000-000000000046")]
    public interface IOleWindow
    {
        /// <summary>
        /// Retrieves the handle to the window associated with the object.
        /// </summary>
        void GetWindow(out IntPtr phwnd);

        /// <summary>
        /// Determines whether context-sensitive help mode should be entered.
        /// </summary>
        void ContextSensitiveHelp(bool fEnterMode);
    }
}
