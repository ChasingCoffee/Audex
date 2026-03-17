using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using Audex.FileReader;
using Audex.UI;
using FluentAssertions;
using Xunit;

namespace Audex.Tests
{
    public class PreviewWindowLifecycleTests
    {
        [Fact]
        public void UpdateContent_ClearsTransientFileState_AndInvalidatesBackgroundWork()
        {
            RunInSta(() =>
            {
                using var window = new PreviewWindow();

                SetPrivateField(window, "_currentAudioData", new byte[] { 1, 2, 3 });
                SetPrivateField(window, "_currentCacheKey", "old-cache");
                SetPrivateField(window, "_isModuleFormat", true);
                SetPrivateField(window, "_currentDuration", 42.5);
                SetPrivateField(window, "_hasBpmTag", true);
                SetPrivateField(window, "_hasKeyTag", true);
                SetPrivateField(window, "_waveCts", new CancellationTokenSource());
                SetPrivateField(window, "_analysisCts", new CancellationTokenSource());
                SetPrivateField(window, "_currentGenerationId", 7);
                SetPrivateField(window, "_currentAnalysisId", 11);

                window.UpdateContent(new AudioFileInfo { FileName = "new.mp3" });

                GetPrivateField<byte[]?>(window, "_currentAudioData").Should().BeNull();
                GetPrivateField<string?>(window, "_currentCacheKey").Should().BeNull();
                GetPrivateField<bool>(window, "_isModuleFormat").Should().BeFalse();
                GetPrivateField<double>(window, "_currentDuration").Should().Be(0);
                GetPrivateField<bool>(window, "_hasBpmTag").Should().BeFalse();
                GetPrivateField<bool>(window, "_hasKeyTag").Should().BeFalse();
                GetPrivateField<CancellationTokenSource?>(window, "_waveCts").Should().BeNull();
                GetPrivateField<CancellationTokenSource?>(window, "_analysisCts").Should().BeNull();
                GetPrivateField<int>(window, "_currentGenerationId").Should().BeGreaterThan(7);
                GetPrivateField<int>(window, "_currentAnalysisId").Should().BeGreaterThan(11);
            });
        }

        [Fact]
        public void StartWaveformGeneration_ModuleFormatWithPeakCache_DoesNotSpawnBackgroundGeneration()
        {
            byte[] audioData = Encoding.UTF8.GetBytes("module-cache-" + Guid.NewGuid().ToString("N"));
            string cacheKey = WaveformCache.ComputeCacheKey(audioData);
            string peakPath = WaveformCache.GetCachePath(cacheKey);
            string colorPath = Path.Combine(Path.GetTempPath(), "Audex", cacheKey + ".wfc");
            float[] peaks = { 0.1f, 0.4f, 0.7f };

            try
            {
                WaveformCache.WriteCache(cacheKey, peaks);
                TryDelete(colorPath);

                RunInSta(() =>
                {
                    using var window = new PreviewWindow();

                    window.StartWaveformGeneration(audioData, totalDuration: 8.0, isModuleFormat: true);

                    GetPrivateField<float[]?>(window, "_waveformPeaks").Should().Equal(peaks);
                    GetPrivateField<int>(window, "_waveformBarsReady").Should().Be(peaks.Length);
                    GetPrivateField<System.Drawing.Color[]?>(window, "_waveformColors").Should().BeNull();
                    GetPrivateField<CancellationTokenSource?>(window, "_waveCts").Should().BeNull();
                });
            }
            finally
            {
                TryDelete(peakPath);
                TryDelete(colorPath);
            }
        }

        private static void RunInSta(Action action)
        {
            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull();
            return (T)field!.GetValue(instance)!;
        }

        private static void SetPrivateField(object instance, string fieldName, object? value)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull();
            field!.SetValue(instance, value);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}
