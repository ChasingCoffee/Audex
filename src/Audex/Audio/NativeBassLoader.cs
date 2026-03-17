using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Audex.Utils;

namespace Audex.Audio
{
    /// <summary>
    /// Loads core native BASS DLLs from the Audex assembly directory without mutating
    /// process-wide DLL search paths (important when running inside prevhost.exe).
    /// </summary>
    internal static class NativeBassLoader
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        private static readonly object _lock = new object();
        private static bool _coreLoaded;
        private static readonly List<IntPtr> _handles = new List<IntPtr>();

        private static readonly string[] CoreDlls =
        {
            "bass.dll",
            "basswasapi.dll",
            "bassmix.dll",
            "bassflac.dll",
            "bass_fx.dll"
        };

        /// <summary>
        /// Loads required native BASS libraries from the given directory.
        /// Safe to call multiple times.
        /// </summary>
        public static bool LoadCoreLibraries(string directory)
        {
            if (_coreLoaded) return true;

            lock (_lock)
            {
                if (_coreLoaded) return true;

                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    Logger.Error($"[NativeBassLoader] Invalid native DLL directory: '{directory}'");
                    return false;
                }

                foreach (string dllName in CoreDlls)
                {
                    string fullPath = Path.Combine(directory, dllName);
                    if (!File.Exists(fullPath))
                    {
                        Logger.Error($"[NativeBassLoader] Missing required native library: {fullPath}");
                        return false;
                    }

                    IntPtr handle = LoadLibrary(fullPath);
                    if (handle == IntPtr.Zero)
                    {
                        int error = Marshal.GetLastWin32Error();
                        Logger.Error($"[NativeBassLoader] LoadLibrary failed for '{fullPath}' (Win32={error})");
                        return false;
                    }

                    _handles.Add(handle);
                }

                _coreLoaded = true;
                Logger.Info($"[NativeBassLoader] Loaded core native BASS libraries from: {directory}");
                return true;
            }
        }
    }
}
