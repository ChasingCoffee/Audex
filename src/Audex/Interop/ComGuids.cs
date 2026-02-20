using System;

namespace Audex.Interop
{
    /// <summary>
    /// COM GUIDs used by the preview handler.
    /// </summary>
    public static class ComGuids
    {
        /// <summary>
        /// CLSID for the AudioPreviewHandler COM class.
        /// </summary>
        public const string AudioPreviewHandler = "F2A5B8C3-4D7E-4A9B-8C1F-3E6D5A7B9C2E";

        /// <summary>
        /// AppID for prevhost.exe (the preview handler host process).
        /// </summary>
        public const string PrevHostAppId = "6d2b5079-2f0b-48dd-ab7f-97cec514d30b";

        /// <summary>
        /// IID for IPreviewHandler interface.
        /// </summary>
        public const string IPreviewHandler = "8895b1c6-b41f-4c1c-a562-0d564250836f";

        /// <summary>
        /// IID for IInitializeWithStream interface.
        /// </summary>
        public const string IInitializeWithStream = "b824b49d-22ac-4161-ac8a-9916e8fa3f7f";

        /// <summary>
        /// IID for IObjectWithSite interface.
        /// </summary>
        public const string IObjectWithSite = "fc4801a3-2ba9-11cf-a229-00aa003d7352";

        /// <summary>
        /// IID for IOleWindow interface.
        /// </summary>
        public const string IOleWindow = "00000114-0000-0000-C000-000000000046";

        /// <summary>
        /// IID for IPreviewHandlerFrame interface.
        /// </summary>
        public const string IPreviewHandlerFrame = "fec87aaf-35f9-447a-adb7-20234fb69178";
    }
}
