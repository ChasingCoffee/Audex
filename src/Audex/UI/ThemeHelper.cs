using System;
using System.Drawing;
using Microsoft.Win32;

namespace Audex.UI
{
    /// <summary>
    /// Static helper for Windows theme detection and theme-appropriate colors.
    /// Queries the system registry to detect dark/light mode and provides color palettes.
    /// </summary>
    public static class ThemeHelper
    {
        private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightThemeValueName = "AppsUseLightTheme";

        // Cached dark mode state — avoids dozens of registry reads per paint cycle.
        // Re-read at most every 2 seconds (theme changes are rare; user sees update within 2s).
        private static bool _cachedIsDarkMode;
        private static DateTime _cacheExpiry = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Determines if the system is in dark mode.
        /// Queries HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme.
        /// Returns true if value is 0 (dark mode), false otherwise.
        /// Result is cached for 2 seconds to avoid excessive registry reads during rendering.
        /// </summary>
        public static bool IsSystemInDarkMode()
        {
            DateTime now = DateTime.UtcNow;
            if (now < _cacheExpiry)
                return _cachedIsDarkMode;

            bool result = false;
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath))
                {
                    if (key != null)
                    {
                        object? value = key.GetValue(AppsUseLightThemeValueName);
                        if (value is int intValue)
                        {
                            // 0 = dark mode, 1 = light mode
                            result = intValue == 0;
                        }
                    }
                }
            }
            catch
            {
                // If we can't read the registry, default to light mode
            }

            _cachedIsDarkMode = result;
            _cacheExpiry = now + CacheDuration;
            return result;
        }

        /// <summary>
        /// Gets the background color for the current theme.
        /// Dark: rgb(32, 32, 32), Light: rgb(255, 255, 255)
        /// </summary>
        public static Color GetBackgroundColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(32, 32, 32)
                : Color.FromArgb(255, 255, 255);
        }

        /// <summary>
        /// Gets the primary text color for the current theme.
        /// Dark: rgb(255, 255, 255), Light: rgb(0, 0, 0)
        /// </summary>
        public static Color GetTextColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(255, 255, 255)
                : Color.FromArgb(0, 0, 0);
        }

        /// <summary>
        /// Gets the secondary text color for labels and secondary information.
        /// Dark: rgb(170, 170, 170), Light: rgb(100, 100, 100)
        /// </summary>
        public static Color GetSecondaryTextColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(170, 170, 170)
                : Color.FromArgb(100, 100, 100);
        }

        /// <summary>
        /// Gets the placeholder color for grayed-out skeleton areas.
        /// Dark: rgb(50, 50, 50), Light: rgb(230, 230, 230)
        /// </summary>
        public static Color GetPlaceholderColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(50, 50, 50)
                : Color.FromArgb(230, 230, 230);
        }

        /// <summary>
        /// Gets the error banner background color (subtle red tint).
        /// Dark: rgb(60, 30, 30), Light: rgb(255, 240, 240)
        /// </summary>
        public static Color GetErrorBannerBackgroundColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(60, 30, 30)
                : Color.FromArgb(255, 240, 240);
        }

        /// <summary>
        /// Gets the error banner text color.
        /// Dark: rgb(255, 180, 180), Light: rgb(180, 0, 0)
        /// </summary>
        public static Color GetErrorBannerTextColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(255, 180, 180)
                : Color.FromArgb(180, 0, 0);
        }

        /// <summary>
        /// Gets the border color for separator lines.
        /// Dark: rgb(60, 60, 60), Light: rgb(210, 210, 210)
        /// </summary>
        public static Color GetBorderColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(60, 60, 60)
                : Color.FromArgb(210, 210, 210);
        }

        // --- Control bar colors ---

        /// <summary>
        /// Gets the control bar background color (same as main background).
        /// </summary>
        public static Color GetControlBarBackgroundColor()
        {
            return GetBackgroundColor();
        }

        /// <summary>
        /// Gets the seek bar unfilled track color.
        /// Dark: rgb(70,70,70), Light: rgb(200,200,200)
        /// </summary>
        public static Color GetSeekBarTrackColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(70, 70, 70)
                : Color.FromArgb(200, 200, 200);
        }

        /// <summary>
        /// Gets the seek bar filled/elapsed portion color (Windows accent blue).
        /// Dark: rgb(0,120,215), Light: rgb(0,100,200)
        /// </summary>
        public static Color GetSeekBarFillColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(0, 120, 215)
                : Color.FromArgb(0, 100, 200);
        }

        /// <summary>
        /// Gets the seek bar thumb circle color.
        /// Dark: rgb(255,255,255), Light: rgb(0,100,200)
        /// </summary>
        public static Color GetSeekBarThumbColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(255, 255, 255)
                : Color.FromArgb(0, 100, 200);
        }

        /// <summary>
        /// Gets the button icon color (same as primary text color).
        /// </summary>
        public static Color GetButtonColor()
        {
            return GetTextColor();
        }

        /// <summary>
        /// Gets the button hover background color.
        /// Dark: rgb(60,60,60), Light: rgb(230,230,230)
        /// </summary>
        public static Color GetButtonHoverColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(60, 60, 60)
                : Color.FromArgb(230, 230, 230);
        }

        /// <summary>
        /// Gets the button pressed background color.
        /// Dark: rgb(80,80,80), Light: rgb(210,210,210)
        /// </summary>
        public static Color GetButtonPressColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(80, 80, 80)
                : Color.FromArgb(210, 210, 210);
        }

        /// <summary>
        /// Gets the volume slider track color (same as seek track).
        /// </summary>
        public static Color GetVolumeTrackColor()
        {
            return GetSeekBarTrackColor();
        }

        /// <summary>
        /// Gets the volume slider filled portion color (less prominent than seek).
        /// Dark: rgb(200,200,200), Light: rgb(100,100,100)
        /// </summary>
        public static Color GetVolumeFillColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(200, 200, 200)
                : Color.FromArgb(100, 100, 100);
        }

        // --- Waveform colors ---

        /// <summary>
        /// Gets the waveform panel background color (slightly offset from Explorer background).
        /// Dark: rgb(28, 28, 28), Light: rgb(248, 248, 250)
        /// </summary>
        public static Color GetWaveformBackgroundColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(28, 28, 28)
                : Color.FromArgb(248, 248, 250);
        }

        /// <summary>
        /// Gets the waveform bar color based on amplitude (0–1).
        /// Cool tones: dark blue (quiet) to bright cyan (loud).
        /// Dark theme: lerp from (20, 60, 140) to (0, 220, 255).
        /// Light theme: lerp from (10, 30, 80) to (0, 180, 210).
        /// </summary>
        public static Color GetWaveformBarColor(float amplitude)
        {
            float t = Math.Max(0f, Math.Min(1f, amplitude));
            if (IsSystemInDarkMode())
            {
                int r = (int)(20 + (0 - 20) * t);
                int g = (int)(60 + (220 - 60) * t);
                int b = (int)(140 + (255 - 140) * t);
                return Color.FromArgb(r, g, b);
            }
            else
            {
                int r = (int)(10 + (0 - 10) * t);
                int g = (int)(30 + (180 - 30) * t);
                int b = (int)(80 + (210 - 80) * t);
                return Color.FromArgb(r, g, b);
            }
        }

        /// <summary>
        /// Gets the played (dimmed) bar color — same hue as GetWaveformBarColor but at 55% opacity (alpha=140).
        /// </summary>
        public static Color GetWaveformPlayedBarColor(float amplitude)
        {
            Color full = GetWaveformBarColor(amplitude);
            return Color.FromArgb(140, full.R, full.G, full.B);
        }

        /// <summary>
        /// Gets the waveform center line color.
        /// Dark: rgb(55, 55, 55), Light: rgb(220, 220, 220)
        /// </summary>
        public static Color GetWaveformCenterLineColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(55, 55, 55)
                : Color.FromArgb(220, 220, 220);
        }

        /// <summary>
        /// Gets the playhead color (white in both themes — stands out on dark background,
        /// contrasts with blue bars on light background).
        /// </summary>
        public static Color GetWaveformPlayheadColor()
        {
            return Color.FromArgb(255, 255, 255);
        }

        /// <summary>
        /// Gets the hover guide line color (semi-transparent).
        /// Dark: rgba(255, 255, 255, 80), Light: rgba(0, 0, 0, 50)
        /// </summary>
        public static Color GetWaveformGuideLineColor()
        {
            return IsSystemInDarkMode()
                ? Color.FromArgb(80, 255, 255, 255)
                : Color.FromArgb(50, 0, 0, 0);
        }

        /// <summary>
        /// Gets the time label color for waveform start/end labels.
        /// Same as GetSecondaryTextColor().
        /// </summary>
        public static Color GetWaveformTimeLabelColor()
        {
            return GetSecondaryTextColor();
        }

        // --- Toggle button colors ---

        /// <summary>Toggle button background (semi-transparent overlay on waveform).</summary>
        public static Color GetToggleButtonBackground()
            => IsSystemInDarkMode() ? Color.FromArgb(140, 40, 40, 42) : Color.FromArgb(140, 235, 235, 238);

        /// <summary>Toggle button hover background.</summary>
        public static Color GetToggleButtonHoverColor()
            => IsSystemInDarkMode() ? Color.FromArgb(180, 60, 60, 64) : Color.FromArgb(180, 215, 215, 218);

        /// <summary>Toggle button pressed background.</summary>
        public static Color GetToggleButtonPressColor()
            => IsSystemInDarkMode() ? Color.FromArgb(200, 80, 80, 84) : Color.FromArgb(200, 195, 195, 198);

        /// <summary>Toggle button icon color.</summary>
        public static Color GetToggleButtonIconColor()
            => IsSystemInDarkMode() ? Color.FromArgb(200, 200, 200) : Color.FromArgb(80, 80, 80);

        // --- Settings overlay colors ---

        /// <summary>
        /// Settings overlay background color (semi-transparent).
        /// Dark: rgba(30,30,30,230), Light: rgba(250,250,250,240)
        /// </summary>
        public static Color SettingsOverlayBackground(bool isDark)
            => isDark
                ? Color.FromArgb(230, 30, 30, 30)
                : Color.FromArgb(240, 250, 250, 250);

        /// <summary>
        /// Settings overlay body text color.
        /// Dark: rgb(220,220,220), Light: rgb(50,50,50)
        /// </summary>
        public static Color SettingsOverlayText(bool isDark)
            => isDark ? Color.FromArgb(220, 220, 220) : Color.FromArgb(50, 50, 50);

        /// <summary>
        /// Settings overlay section header text color (slightly brighter/bolder).
        /// Dark: rgb(150,150,150), Light: rgb(100,100,100)
        /// </summary>
        public static Color SettingsOverlaySectionHeader(bool isDark)
            => isDark ? Color.FromArgb(150, 150, 155) : Color.FromArgb(100, 100, 110);

        /// <summary>
        /// Settings overlay control background (toggle track, button, dropdown).
        /// Dark: rgb(55,55,58), Light: rgb(235,235,238)
        /// </summary>
        public static Color SettingsOverlayControl(bool isDark)
            => isDark ? Color.FromArgb(55, 55, 58) : Color.FromArgb(235, 235, 238);

        /// <summary>
        /// Settings overlay active/selected control color (accent blue).
        /// </summary>
        public static Color SettingsOverlayControlActive(bool isDark)
            => isDark ? Color.FromArgb(0, 120, 215) : Color.FromArgb(0, 100, 200);

        /// <summary>
        /// Settings overlay section divider line color.
        /// Dark: rgb(60,60,65), Light: rgb(210,210,215)
        /// </summary>
        public static Color SettingsOverlayDivider(bool isDark)
            => isDark ? Color.FromArgb(60, 60, 65) : Color.FromArgb(210, 210, 215);

        /// <summary>
        /// Settings overlay button hover state background.
        /// Dark: rgb(70,70,75), Light: rgb(220,220,225)
        /// </summary>
        public static Color SettingsOverlayButtonHover(bool isDark)
            => isDark ? Color.FromArgb(70, 70, 75) : Color.FromArgb(220, 220, 225);
    }
}
