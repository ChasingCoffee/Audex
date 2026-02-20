using System;
using System.Collections.Generic;
using System.IO;
using ManagedBass;
using Audex.Utils;

namespace Audex.Audio
{
    /// <summary>
    /// Manages BASS plugin DLLs for extended format support.
    /// Tracks which plugins are loaded and exposes format capability queries.
    /// </summary>
    public static class PluginManager
    {
        // Handles for loaded plugins keyed by plugin name (without extension)
        private static readonly Dictionary<string, int> _loadedPlugins = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Plugin DLL names to the audio extensions they provide
        private static readonly Dictionary<string, string[]> _pluginFormats = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "bass_aac",  new[] { ".aac", ".m4a" } },
            { "basswma",   new[] { ".wma" } },
            { "bassopus",  new[] { ".opus" } },
        };

        // Extensions natively supported by BASS without any plugin
        private static readonly HashSet<string> _coreExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".wav", ".mp3", ".flac", ".aiff", ".aif", ".ogg"
        };

        // Module format extensions — use MusicLoad, not CreateStream
        private static readonly HashSet<string> _moduleExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mod", ".xm", ".it", ".s3m"
        };

        /// <summary>
        /// Loads all available BASS plugin DLLs from the specified directory.
        /// Logs success/failure for each plugin. Safe to call multiple times.
        /// </summary>
        /// <param name="dllDirectory">Directory containing plugin DLL files (typically the assembly directory).</param>
        public static void LoadPlugins(string dllDirectory)
        {
            foreach (var kvp in _pluginFormats)
            {
                string pluginName = kvp.Key;
                string dllPath = Path.Combine(dllDirectory, pluginName + ".dll");

                if (!File.Exists(dllPath))
                {
                    Logger.Info($"[PluginManager] Plugin not found (skipping): {dllPath}");
                    continue;
                }

                // Skip 0-byte placeholder files
                var fi = new FileInfo(dllPath);
                if (fi.Length == 0)
                {
                    Logger.Info($"[PluginManager] Plugin is a 0-byte placeholder (skipping): {pluginName}.dll");
                    continue;
                }

                try
                {
                    int handle = Bass.PluginLoad(dllPath);
                    if (handle != 0)
                    {
                        _loadedPlugins[pluginName] = handle;
                        Logger.Info($"[PluginManager] Loaded plugin: {pluginName}.dll (handle={handle})");
                    }
                    else
                    {
                        Logger.Error($"[PluginManager] Bass.PluginLoad failed for {pluginName}.dll: {Bass.LastError}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[PluginManager] Exception loading plugin {pluginName}.dll: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Returns true if the given file extension can be decoded (either natively by BASS,
        /// via a loaded plugin, or as a module format via MusicLoad).
        /// </summary>
        public static bool IsFormatSupported(string extension)
        {
            string ext = extension?.ToLowerInvariant() ?? string.Empty;

            if (_coreExtensions.Contains(ext)) return true;
            if (_moduleExtensions.Contains(ext)) return true;

            // Check if the extension is provided by a loaded plugin
            foreach (var kvp in _pluginFormats)
            {
                string pluginName = kvp.Key;
                string[] exts = kvp.Value;
                foreach (string pluginExt in exts)
                {
                    if (string.Equals(ext, pluginExt, StringComparison.OrdinalIgnoreCase))
                    {
                        return _loadedPlugins.ContainsKey(pluginName);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Returns null if the format is supported.
        /// Returns a descriptive reason string if the format is not supported.
        /// </summary>
        public static string? GetUnsupportedReason(string extension)
        {
            string ext = extension?.ToLowerInvariant() ?? string.Empty;

            if (_coreExtensions.Contains(ext)) return null;
            if (_moduleExtensions.Contains(ext)) return null;

            // Check plugin formats
            foreach (var kvp in _pluginFormats)
            {
                string pluginName = kvp.Key;
                string[] exts = kvp.Value;
                foreach (string pluginExt in exts)
                {
                    if (string.Equals(ext, pluginExt, StringComparison.OrdinalIgnoreCase))
                    {
                        if (_loadedPlugins.ContainsKey(pluginName))
                            return null; // supported

                        // Map plugin name to a human-readable description
                        string formatName = pluginName switch
                        {
                            "bass_aac"  => "AAC/M4A",
                            "basswma"   => "WMA",
                            "bassopus"  => "OPUS",
                            _           => pluginName.ToUpperInvariant()
                        };
                        return $"{formatName} plugin not found";
                    }
                }
            }

            // Completely unknown extension
            return string.IsNullOrEmpty(ext)
                ? "Unsupported format"
                : $"Unsupported format: {ext.TrimStart('.')}";
        }

        /// <summary>
        /// Returns true if the extension is a tracked-music module format
        /// that should use Bass.MusicLoad instead of Bass.CreateStream.
        /// </summary>
        public static bool IsModuleFormat(string extension)
        {
            return _moduleExtensions.Contains(extension?.ToLowerInvariant() ?? string.Empty);
        }
    }
}
