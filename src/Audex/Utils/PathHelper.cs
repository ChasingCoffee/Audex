using System;
using System.IO;

namespace Audex.Utils
{
    /// <summary>
    /// Static utility class for AppData path resolution.
    /// All paths use LOCALAPPDATA for low-integrity process compatibility.
    /// </summary>
    public static class PathHelper
    {
        private const string AppName = "Audex";

        /// <summary>
        /// Gets the root directory for application data.
        /// Returns: %LOCALAPPDATA%\Audex\
        /// </summary>
        public static string GetAppDataRoot()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, AppName);
        }

        /// <summary>
        /// Gets the path to the legacy INI configuration file.
        /// Returns: %LOCALAPPDATA%\Audex\config.ini
        /// </summary>
        public static string GetConfigPath()
        {
            return Path.Combine(GetAppDataRoot(), "config.ini");
        }

        /// <summary>
        /// Gets the path to the JSON configuration file.
        /// Returns: %LOCALAPPDATA%\Audex\config.json
        /// </summary>
        public static string GetJsonConfigPath()
        {
            return Path.Combine(GetAppDataRoot(), "config.json");
        }

        /// <summary>
        /// Gets the directory path for log files.
        /// Returns: %LOCALAPPDATA%\Audex\logs\
        /// </summary>
        public static string GetLogDirectory()
        {
            return Path.Combine(GetAppDataRoot(), "logs");
        }

        /// <summary>
        /// Gets the full path to the log file.
        /// Returns: %LOCALAPPDATA%\Audex\logs\Audex.log
        /// </summary>
        public static string GetLogFilePath()
        {
            return Path.Combine(GetLogDirectory(), "Audex.log");
        }

        /// <summary>
        /// Ensures that the application directories exist.
        /// Creates AppData root and logs subdirectory if they don't exist.
        /// </summary>
        public static void EnsureDirectories()
        {
            try
            {
                string appDataRoot = GetAppDataRoot();
                if (!Directory.Exists(appDataRoot))
                {
                    Directory.CreateDirectory(appDataRoot);
                }

                string logDirectory = GetLogDirectory();
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
            }
            catch (Exception)
            {
                // Swallow directory creation failures - application should continue
                // without logging rather than crashing
            }
        }
    }
}
