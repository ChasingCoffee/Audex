using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using IniParser;
using IniParser.Model;
using Audex.Utils;

namespace Audex.Config
{
    /// <summary>
    /// Reads and writes JSON configuration file with fallback to defaults.
    /// Automatically migrates existing config.ini to config.json on first load.
    /// INI file is preserved in place as backup after migration.
    /// </summary>
    public static class ConfigManager
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented
        };

        /// <summary>
        /// Loads configuration from config.json.
        /// On first load, migrates config.ini to config.json if INI exists and JSON does not.
        /// If neither file exists or JSON has errors, returns defaults.
        /// </summary>
        public static AppConfig Load()
        {
            try
            {
                MigrateIfNeeded();
            }
            catch (Exception ex)
            {
                Logger.Error($"Config migration failed: {ex.Message}", ex);
            }

            string jsonPath = PathHelper.GetJsonConfigPath();

            if (!File.Exists(jsonPath))
            {
                return new AppConfig();
            }

            try
            {
                string json = ReadAllTextWithRetry(jsonPath);
                AppConfig? config = JsonConvert.DeserializeObject<AppConfig>(json);
                if (config == null)
                    return new AppConfig();

                // Clamp volume to valid range
                config.Volume = Math.Max(0.0f, Math.Min(1.0f, config.Volume));

                // Normalize key profile selection
                if (string.IsNullOrWhiteSpace(config.KeyDetectionProfile))
                    config.KeyDetectionProfile = "auto";
                else
                    config.KeyDetectionProfile = config.KeyDetectionProfile.Trim().ToLowerInvariant();

                // Deduplicate extensions (migration could produce duplicates)
                config.SupportedExtensions = config.SupportedExtensions.Distinct().ToList();

                Logger.Info($"Configuration loaded from {jsonPath}");
                return config;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to parse JSON configuration: {ex.Message}", ex);
                return new AppConfig();
            }
        }

        /// <summary>
        /// Saves the configuration to config.json.
        /// Ensures the config directory exists before writing.
        /// Writes to a temp file and atomically swaps it into place, so a concurrent reader
        /// (e.g. another prevhost.exe instance previewing a different file at the same time)
        /// never observes a partially-written file.
        /// </summary>
        public static void Save(AppConfig config)
        {
            string jsonPath = PathHelper.GetJsonConfigPath();
            string configDir = Path.GetDirectoryName(jsonPath)!;
            string? tempPath = null;

            try
            {
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                string json = JsonConvert.SerializeObject(config, JsonSettings);

                tempPath = jsonPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tempPath, json);

                if (File.Exists(jsonPath))
                    File.Replace(tempPath, jsonPath, null);
                else
                    File.Move(tempPath, jsonPath);
                tempPath = null; // consumed by Replace/Move

                Logger.Info($"Configuration saved to {jsonPath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save configuration: {ex.Message}", ex);
            }
            finally
            {
                if (tempPath != null)
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        /// <summary>
        /// Reads a file's full text, retrying briefly on IOException. Guards against the narrow
        /// window where a concurrent process's atomic Save (File.Replace) holds a transient
        /// exclusive handle on the same path.
        /// </summary>
        private static string ReadAllTextWithRetry(string path)
        {
            const int maxAttempts = 3;
            for (int attempt = 1; attempt < maxAttempts; attempt++)
            {
                try
                {
                    return File.ReadAllText(path);
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(15);
                }
            }

            return File.ReadAllText(path); // let a final failure propagate normally
        }

        /// <summary>
        /// Gets the list of supported extensions from the configuration.
        /// </summary>
        public static List<string> GetExtensions()
        {
            var config = Load();
            return config.SupportedExtensions;
        }

        // -------------------------------------------------------------------------
        // INI migration (runs once: config.ini exists AND config.json does not)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Migrates config.ini to config.json if the INI file exists and the JSON file does not.
        /// The INI file is deleted after successful migration.
        /// </summary>
        private static void MigrateIfNeeded()
        {
            string iniPath = PathHelper.GetConfigPath();
            string jsonPath = PathHelper.GetJsonConfigPath();

            // Only migrate when INI exists and JSON does not
            if (!File.Exists(iniPath) || File.Exists(jsonPath))
                return;

            try
            {
                AppConfig migrated = LoadFromIni(iniPath);

                string configDir = Path.GetDirectoryName(jsonPath)!;
                if (!Directory.Exists(configDir))
                    Directory.CreateDirectory(configDir);

                string json = JsonConvert.SerializeObject(migrated, JsonSettings);
                File.WriteAllText(jsonPath, json);

                // Delete the old INI file after successful migration
                File.Delete(iniPath);
                Logger.Info($"Migrated config.ini to config.json (INI deleted)");
            }
            catch (Exception ex)
            {
                Logger.Error($"Migration from INI failed: {ex.Message}", ex);
                // Do not rethrow — caller will fall back to defaults
            }
        }

        /// <summary>
        /// Reads an AppConfig from a legacy config.ini file.
        /// Used only during the one-time migration path.
        /// </summary>
        private static AppConfig LoadFromIni(string iniPath)
        {
            var config = new AppConfig();

            var parser = new FileIniDataParser();
            IniData data = parser.ReadFile(iniPath);

            // [Formats] section (takes precedence over legacy [FileTypes])
            bool formatsLoaded = false;
            if (data.Sections.ContainsSection("Formats"))
            {
                string? extensionsValue = data["Formats"]["Extensions"];
                if (!string.IsNullOrWhiteSpace(extensionsValue))
                {
                    config.SupportedExtensions = ParseExtensions(extensionsValue);
                    formatsLoaded = true;
                }
            }

            if (!formatsLoaded && data.Sections.ContainsSection("FileTypes"))
            {
                string? extensionsValue = data["FileTypes"]["Extensions"];
                if (!string.IsNullOrWhiteSpace(extensionsValue))
                {
                    config.SupportedExtensions = ParseExtensions(extensionsValue);
                }
            }

            // [Logging] section
            if (data.Sections.ContainsSection("Logging"))
            {
                string? logLevel = data["Logging"]["Level"];
                if (!string.IsNullOrWhiteSpace(logLevel))
                    config.LogLevel = logLevel.ToLowerInvariant();
            }

            // [Performance] section
            if (data.Sections.ContainsSection("Performance"))
            {
                string? debounceValue = data["Performance"]["DebounceMs"];
                if (!string.IsNullOrWhiteSpace(debounceValue) && int.TryParse(debounceValue, out int debounceMs))
                    config.DebounceMs = debounceMs;
            }

            // [Audio] section
            if (data.Sections.ContainsSection("Audio"))
            {
                string? volumeValue = data["Audio"]["Volume"];
                if (!string.IsNullOrWhiteSpace(volumeValue) &&
                    float.TryParse(volumeValue, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float volume))
                {
                    config.Volume = Math.Max(0.0f, Math.Min(1.0f, volume));
                }

                string? mutedValue = data["Audio"]["IsMuted"];
                if (!string.IsNullOrWhiteSpace(mutedValue) && bool.TryParse(mutedValue, out bool isMuted))
                    config.IsMuted = isMuted;
            }

            // [Waveform] section
            if (data.Sections.ContainsSection("Waveform"))
            {
                string? colorMode = data["Waveform"]["ColorMode"];
                if (!string.IsNullOrWhiteSpace(colorMode) && bool.TryParse(colorMode, out bool cm))
                    config.WaveformColorMode = cm;
            }

            // [Analysis] section
            if (data.Sections.ContainsSection("Analysis"))
            {
                string? enableDetection = data["Analysis"]["EnableBpmKeyDetection"];
                if (!string.IsNullOrWhiteSpace(enableDetection) && bool.TryParse(enableDetection, out bool ed))
                    config.EnableBpmKeyDetection = ed;
            }

            return config;
        }

        /// <summary>
        /// Parses a comma-separated list of extensions.
        /// Extensions are normalized to lowercase and trimmed.
        /// </summary>
        private static List<string> ParseExtensions(string extensionsValue)
        {
            return extensionsValue
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ext => ext.Trim().ToLowerInvariant())
                .Where(ext => !string.IsNullOrWhiteSpace(ext))
                .Select(ext => ext.StartsWith(".") ? ext : "." + ext)
                .ToList();
        }
    }
}
