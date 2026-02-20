using System;
using System.Runtime.InteropServices;

namespace Audex.Interop
{
    /// <summary>
    /// IPreviewHandler interface - Main interface for implementing preview handlers.
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("8895b1c6-b41f-4c1c-a562-0d564250836f")]
    public interface IPreviewHandler
    {
        /// <summary>
        /// Sets the window handle and bounding rectangle for the preview.
        /// </summary>
        void SetWindow(IntPtr hwnd, ref RECT rect);

        /// <summary>
        /// Updates the bounding rectangle of the preview.
        /// </summary>
        void SetRect(ref RECT rect);

        /// <summary>
        /// Directs the preview handler to begin rendering the content.
        /// </summary>
        void DoPreview();

        /// <summary>
        /// Directs the preview handler to cease rendering and release resources.
        /// </summary>
        void Unload();

        /// <summary>
        /// Directs the preview handler to set focus to itself.
        /// </summary>
        void SetFocus();

        /// <summary>
        /// Retrieves the window handle of the previewer window.
        /// </summary>
        void QueryFocus(out IntPtr phwnd);

        /// <summary>
        /// Directs the preview handler to handle a keyboard accelerator.
        /// </summary>
        [PreserveSig]
        uint TranslateAccelerator(ref MSG pmsg);
    }
}
