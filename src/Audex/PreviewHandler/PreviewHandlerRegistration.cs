using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Audex.Config;
using Audex.Interop;
using Audex.Utils;

namespace Audex.PreviewHandler
{
    /// <summary>
    /// COM registration and unregistration logic for the AudioPreviewHandler.
    /// Handles registry entries for preview handler and prevhost.exe AppID.
    /// </summary>
    public static class PreviewHandlerRegistration
    {
        private static string PreviewHandlerShellExPath => $@"shellex\{{{ComGuids.IPreviewHandler}}}";

        /// <summary>
        /// Registers the AudioPreviewHandler COM class.
        /// Called by regasm.exe or regsvr32 during installation.
        /// </summary>
        [ComRegisterFunction]
        public static void Register(Type type)
        {
            try
            {
                try { Logger.Initialize(); } catch { /* Logger may fail in regasm context */ }
                try { Logger.Info($"Registering preview handler: {type.FullName}"); } catch { }

                string clsid = ComGuids.AudioPreviewHandler;
                string appId = ComGuids.PrevHostAppId;

                // HKCR\CLSID\{CLSID}
                using (RegistryKey clsidKey = Registry.ClassesRoot.CreateSubKey($@"CLSID\{{{clsid}}}")!)
                {
                    clsidKey.SetValue(null, "Audex Audio Preview Handler");
                    clsidKey.SetValue("DisplayName", "Audex");
                    clsidKey.SetValue("AppID", $"{{{appId}}}");

                    // DisableLowILProcessIsolation — required for .NET CLR in prevhost.exe low-integrity
                    clsidKey.SetValue("DisableLowILProcessIsolation", 1, RegistryValueKind.DWord);

                    // InprocServer32 subkey (automatically created by regasm, but we'll set threading model)
                    using (RegistryKey inprocKey = clsidKey.CreateSubKey("InprocServer32")!)
                    {
                        inprocKey.SetValue("ThreadingModel", "Apartment");
                    }
                }

                // HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers
                // Register the preview handler globally
                try
                {
                    using (RegistryKey previewHandlersKey = Registry.LocalMachine.CreateSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers")!)
                    {
                        previewHandlersKey.SetValue($"{{{clsid}}}", "Audex");
                        Logger.Info("Registered in PreviewHandlers (HKLM)");
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    Logger.Warn("Cannot write to HKLM PreviewHandlers - requires admin privileges");
                    // Try HKCU instead
                    using (RegistryKey previewHandlersKey = Registry.CurrentUser.CreateSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers")!)
                    {
                        previewHandlersKey.SetValue($"{{{clsid}}}", "Audex");
                        Logger.Info("Registered in PreviewHandlers (HKCU)");
                    }
                }

                // Register file extension associations from config
                List<string> extensions = ConfigManager.Load().SupportedExtensions;
                foreach (string extension in extensions)
                {
                    RegisterExtension(extension, clsid);
                }

                Logger.Info("Preview handler registration complete");
            }
            catch (Exception ex)
            {
                Logger.Error($"Registration failed: {ex.Message}", ex);
                // Don't throw - allow registration to complete partially
                // Installer can check registry to verify success
            }
        }

        /// <summary>
        /// Unregisters the AudioPreviewHandler COM class.
        /// Called by regasm.exe /u or regsvr32 /u during uninstallation.
        /// </summary>
        [ComUnregisterFunction]
        public static void Unregister(Type type)
        {
            try
            {
                Logger.Initialize();
                Logger.Info($"Unregistering preview handler: {type.FullName}");

                string clsid = ComGuids.AudioPreviewHandler;

                // Remove from HKLM PreviewHandlers
                try
                {
                    using (RegistryKey? previewHandlersKey = Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers", true))
                    {
                        previewHandlersKey?.DeleteValue($"{{{clsid}}}", false);
                        Logger.Info("Removed from PreviewHandlers (HKLM)");
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    Logger.Warn("Cannot access HKLM PreviewHandlers - requires admin privileges");
                }

                // Remove from HKCU PreviewHandlers
                try
                {
                    using (RegistryKey? previewHandlersKey = Registry.CurrentUser.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\PreviewHandlers", true))
                    {
                        previewHandlersKey?.DeleteValue($"{{{clsid}}}", false);
                        Logger.Info("Removed from PreviewHandlers (HKCU)");
                    }
                }
                catch
                {
                    // Ignore - may not exist
                }

                // Remove CLSID key
                try
                {
                    Registry.ClassesRoot.DeleteSubKeyTree($@"CLSID\{{{clsid}}}", false);
                    Logger.Info("Removed CLSID registry key");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Could not remove CLSID key: {ex.Message}");
                }

                // Unregister file extension associations
                List<string> extensions = ConfigManager.Load().SupportedExtensions;
                foreach (string extension in extensions)
                {
                    UnregisterExtension(extension, clsid);
                }

                Logger.Info("Preview handler unregistration complete");
            }
            catch (Exception ex)
            {
                Logger.Error($"Unregistration failed: {ex.Message}", ex);
                // Don't throw - allow unregistration to complete partially
            }
        }

        /// <summary>
        /// Registers a file extension to use this preview handler.
        /// </summary>
        private static void RegisterExtension(string extension, string clsid)
        {
            try
            {
                Logger.Info($"Registering extension: {extension}");

                // Always register via SystemFileAssociations to avoid changing file ownership.
                string safeExt = NormalizeExtension(extension);
                if (string.IsNullOrEmpty(safeExt))
                {
                    Logger.Warn("Skipping empty extension entry during registration");
                    return;
                }
                string systemAssocShellExPath = $@"SystemFileAssociations\{safeExt}\{PreviewHandlerShellExPath}";
                using (RegistryKey? shellexKey = Registry.ClassesRoot.CreateSubKey(systemAssocShellExPath))
                {
                    if (shellexKey == null)
                    {
                        Logger.Warn($"Could not create system association key for extension {safeExt}");
                        return;
                    }

                    shellexKey.SetValue(null, $"{{{clsid}}}", RegistryValueKind.String);
                    Logger.Info($"Registered preview handler via SystemFileAssociations for {safeExt}");
                }

                // Optionally register under the existing ProgID when safe to do so.
                string? progId = GetSafeExistingProgId(safeExt);
                if (!string.IsNullOrEmpty(progId))
                {
                    string progIdShellexPath = $@"{progId}\{PreviewHandlerShellExPath}";
                    using (RegistryKey? shellexKey = Registry.ClassesRoot.CreateSubKey(progIdShellexPath))
                    {
                        if (shellexKey != null)
                        {
                            shellexKey.SetValue(null, $"{{{clsid}}}", RegistryValueKind.String);
                            Logger.Info($"Registered preview handler under ProgID {progId} for {safeExt}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to register extension {extension}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Unregisters a file extension from this preview handler.
        /// </summary>
        private static void UnregisterExtension(string extension, string clsid)
        {
            try
            {
                Logger.Info($"Unregistering extension: {extension}");

                string safeExt = NormalizeExtension(extension);
                if (string.IsNullOrEmpty(safeExt))
                {
                    Logger.Warn("Skipping empty extension entry during unregistration");
                    return;
                }

                // Remove SystemFileAssociations entry only when it points to this CLSID.
                string systemAssocShellExPath = $@"SystemFileAssociations\{safeExt}\{PreviewHandlerShellExPath}";
                DeleteShellExEntryIfOwned(systemAssocShellExPath, clsid);

                // Remove ProgID entry only when it points to this CLSID.
                string? progId = GetSafeExistingProgId(safeExt);
                if (!string.IsNullOrEmpty(progId))
                {
                    string progIdShellexPath = $@"{progId}\{PreviewHandlerShellExPath}";
                    DeleteShellExEntryIfOwned(progIdShellexPath, clsid);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to unregister extension {extension}: {ex.Message}", ex);
            }
        }

        private static void DeleteShellExEntryIfOwned(string shellExPath, string clsid)
        {
            try
            {
                using (RegistryKey? shellExKey = Registry.ClassesRoot.OpenSubKey(shellExPath, writable: false))
                {
                    if (shellExKey == null)
                    {
                        Logger.Debug($"Shellex path not found: HKCR\\{shellExPath}");
                        return;
                    }

                    string? currentValue = shellExKey.GetValue(null) as string;
                    if (!IsMatchingClsidValue(currentValue, clsid))
                    {
                        Logger.Debug($"Shellex path not owned by Audex, skipping: HKCR\\{shellExPath}");
                        return;
                    }
                }

                Registry.ClassesRoot.DeleteSubKey(shellExPath, false);
                Logger.Info($"Removed preview handler mapping: HKCR\\{shellExPath}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not remove shellex path HKCR\\{shellExPath}: {ex.Message}");
            }
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return string.Empty;

            string normalized = extension.Trim().ToLowerInvariant();
            return normalized.StartsWith(".", StringComparison.Ordinal) ? normalized : "." + normalized;
        }

        private static string? GetSafeExistingProgId(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return null;

            try
            {
                using (RegistryKey? extKey = Registry.ClassesRoot.OpenSubKey(extension, writable: false))
                {
                    string? progId = extKey?.GetValue(null) as string;
                    if (string.IsNullOrWhiteSpace(progId))
                        return null;

                    string normalizedProgId = progId!.Trim();
                    if (normalizedProgId.StartsWith("AppX", StringComparison.OrdinalIgnoreCase))
                        return null;

                    if (IsUserChoiceProgId(extension, normalizedProgId))
                        return null;

                    return normalizedProgId;
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool IsUserChoiceProgId(string extension, string progId)
        {
            try
            {
                string userChoicePath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice";
                using (RegistryKey? userChoiceKey = Registry.CurrentUser.OpenSubKey(userChoicePath, writable: false))
                {
                    string? userChoiceProgId = userChoiceKey?.GetValue("ProgId") as string;
                    if (string.IsNullOrWhiteSpace(userChoiceProgId))
                        return false;

                    string normalizedUserChoiceProgId = userChoiceProgId!.Trim();
                    return string.Equals(normalizedUserChoiceProgId, progId, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsMatchingClsidValue(string? actualValue, string clsid)
        {
            if (string.IsNullOrWhiteSpace(actualValue))
                return false;

            string normalizedActualValue = actualValue!.Trim();
            string expected = $"{{{clsid}}}";
            return string.Equals(normalizedActualValue, expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
