using System;
using System.Collections.Generic;
using System.Reflection;
using Audex.Audio;
using FluentAssertions;
using Xunit;

namespace Audex.Tests
{
    public class PluginManagerTests : IDisposable
    {
        private static readonly FieldInfo LoadedPluginsField = typeof(PluginManager)
            .GetField("_loadedPlugins", BindingFlags.NonPublic | BindingFlags.Static)!;

        private readonly Dictionary<string, int> _originalLoadedPlugins;

        public PluginManagerTests()
        {
            var loaded = GetLoadedPlugins();
            _originalLoadedPlugins = new Dictionary<string, int>(loaded, StringComparer.OrdinalIgnoreCase);
            loaded.Clear();
        }

        public void Dispose()
        {
            var loaded = GetLoadedPlugins();
            loaded.Clear();
            foreach (var kv in _originalLoadedPlugins)
            {
                loaded[kv.Key] = kv.Value;
            }
        }

        [Theory]
        [InlineData(".wav")]
        [InlineData(".MP3")]
        [InlineData(".FlAc")]
        public void IsFormatSupported_CoreFormats_ReturnTrue(string extension)
        {
            PluginManager.IsFormatSupported(extension).Should().BeTrue();
            PluginManager.GetUnsupportedReason(extension).Should().BeNull();
        }

        [Theory]
        [InlineData(".mod")]
        [InlineData(".XM")]
        [InlineData(".it")]
        [InlineData(".S3M")]
        public void IsFormatSupported_ModuleFormats_ReturnTrue(string extension)
        {
            PluginManager.IsFormatSupported(extension).Should().BeTrue();
            PluginManager.IsModuleFormat(extension).Should().BeTrue();
            PluginManager.GetUnsupportedReason(extension).Should().BeNull();
        }

        [Fact]
        public void PluginBackedFormat_WhenPluginMissing_ReturnsUnsupportedReason()
        {
            PluginManager.IsFormatSupported(".aac").Should().BeFalse();
            PluginManager.GetUnsupportedReason(".aac").Should().Be("AAC/M4A plugin not found");
        }

        [Fact]
        public void PluginBackedFormat_WhenPluginMarkedLoaded_ReturnsSupported()
        {
            var loaded = GetLoadedPlugins();
            loaded["bass_aac"] = 123;

            PluginManager.IsFormatSupported(".aac").Should().BeTrue();
            PluginManager.IsFormatSupported(".m4a").Should().BeTrue();
            PluginManager.GetUnsupportedReason(".aac").Should().BeNull();
            PluginManager.GetUnsupportedReason(".m4a").Should().BeNull();
        }

        [Fact]
        public void UnknownFormat_ReturnsGenericUnsupportedReason()
        {
            PluginManager.IsFormatSupported(".xyz").Should().BeFalse();
            PluginManager.GetUnsupportedReason(".xyz").Should().Be("Unsupported format: xyz");
        }

        [Fact]
        public void EmptyExtension_ReturnsUnsupportedFormatReason()
        {
            PluginManager.IsFormatSupported(string.Empty).Should().BeFalse();
            PluginManager.GetUnsupportedReason(string.Empty).Should().Be("Unsupported format");
        }

        private static Dictionary<string, int> GetLoadedPlugins()
        {
            return (Dictionary<string, int>)LoadedPluginsField.GetValue(null)!;
        }
    }
}
