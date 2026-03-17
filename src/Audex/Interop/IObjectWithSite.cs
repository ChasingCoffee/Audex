using System;
using System.Runtime.InteropServices;

namespace Audex.Interop
{
    /// <summary>
    /// IObjectWithSite interface - Provides a site object to the preview handler.
    /// </summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("fc4801a3-2ba9-11cf-a229-00aa003d7352")]
    public interface IObjectWithSite
    {
        /// <summary>
        /// Sets the site object for the preview handler.
        /// </summary>
        void SetSite([MarshalAs(UnmanagedType.IUnknown)] object pUnkSite);

        /// <summary>
        /// Gets the site object for the preview handler.
        /// </summary>
        void GetSite(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvSite);
    }
}
