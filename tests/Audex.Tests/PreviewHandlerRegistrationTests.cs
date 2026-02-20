using System;
using System.Reflection;
using Audex.Interop;
using Audex.PreviewHandler;
using FluentAssertions;
using Microsoft.Win32;
using Xunit;

namespace Audex.Tests
{
    public class PreviewHandlerRegistrationTests
    {
        private static readonly Type RegistrationType = typeof(PreviewHandlerRegistration);

        [Fact]
        public void RegisterExtension_DoesNotTakeOverExtensionDefaultProgId()
        {
            string extension = ".audexreg" + Guid.NewGuid().ToString("N");
            string extRelativePath = extension;
            string fullExtPath = ToUserClassesPath(extRelativePath);
            const string OriginalProgId = "Audex.Test.OriginalProgId";

            try
            {
                // Simulate a pre-existing file association owner.
                using (var extKey = Registry.CurrentUser.CreateSubKey(fullExtPath))
                {
                    extKey.Should().NotBeNull();
                    extKey!.SetValue(null, OriginalProgId, RegistryValueKind.String);
                }

                InvokePrivateStatic("RegisterExtension", extension, ComGuids.AudioPreviewHandler);

                using (var extKey = Registry.CurrentUser.OpenSubKey(fullExtPath, writable: false))
                {
                    extKey.Should().NotBeNull();
                    extKey!.GetValue(null).Should().Be(OriginalProgId, "registration should not modify extension ownership");
                }
            }
            finally
            {
                DeleteUserClassesKeyTree(extRelativePath);
            }
        }

        [Fact]
        public void UnregisterExtension_DoesNotDeleteNonOwnedSystemAssociationEntry()
        {
            string extension = ".audexunreg" + Guid.NewGuid().ToString("N");
            string systemAssocShellexPath = GetSystemAssociationShellexPath(extension);
            string fullSystemAssocPath = ToUserClassesPath(systemAssocShellexPath);
            const string OtherClsid = "11111111-2222-3333-4444-555555555555";

            try
            {
                using (var shellexKey = Registry.CurrentUser.CreateSubKey(fullSystemAssocPath))
                {
                    shellexKey.Should().NotBeNull();
                    shellexKey!.SetValue(null, $"{{{OtherClsid}}}", RegistryValueKind.String);
                }

                InvokePrivateStatic("UnregisterExtension", extension, ComGuids.AudioPreviewHandler);

                using (var shellexKey = Registry.CurrentUser.OpenSubKey(fullSystemAssocPath, writable: false))
                {
                    shellexKey.Should().NotBeNull("non-owned entries must be preserved");
                    (shellexKey!.GetValue(null) as string).Should().Be($"{{{OtherClsid}}}");
                }
            }
            finally
            {
                DeleteUserClassesKeyTree($@"SystemFileAssociations\{extension}");
            }
        }

        [Fact]
        public void UnregisterExtension_DeletesOwnedSystemAssociationEntry()
        {
            string extension = ".audexowned" + Guid.NewGuid().ToString("N");
            string systemAssocShellexPath = GetSystemAssociationShellexPath(extension);
            string fullSystemAssocPath = ToUserClassesPath(systemAssocShellexPath);

            try
            {
                using (var shellexKey = Registry.CurrentUser.CreateSubKey(fullSystemAssocPath))
                {
                    shellexKey.Should().NotBeNull();
                    shellexKey!.SetValue(null, $"{{{ComGuids.AudioPreviewHandler}}}", RegistryValueKind.String);
                }

                InvokePrivateStatic("UnregisterExtension", extension, ComGuids.AudioPreviewHandler);

                using (var shellexKey = Registry.CurrentUser.OpenSubKey(fullSystemAssocPath, writable: false))
                {
                    shellexKey.Should().BeNull("owned entries should be removed during unregistration");
                }
            }
            finally
            {
                DeleteUserClassesKeyTree($@"SystemFileAssociations\{extension}");
            }
        }

        private static string GetSystemAssociationShellexPath(string extension) =>
            $@"SystemFileAssociations\{extension}\shellex\{{{ComGuids.IPreviewHandler}}}";

        private static string ToUserClassesPath(string classesRootRelativePath) =>
            $@"Software\Classes\{classesRootRelativePath}";

        private static void DeleteUserClassesKeyTree(string classesRootRelativePath)
        {
            using (RegistryKey? classesKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true))
            {
                classesKey?.DeleteSubKeyTree(classesRootRelativePath, throwOnMissingSubKey: false);
            }
        }

        private static void InvokePrivateStatic(string methodName, params object[] args)
        {
            MethodInfo? method = RegistrationType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();
            method!.Invoke(null, args);
        }
    }
}
