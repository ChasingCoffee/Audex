using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using Audex.Config;

namespace Audex.UI
{
    /// <summary>
    /// Hit zones within the settings overlay.
    /// </summary>
    public enum SettingsHitZone
    {
        None,
        CloseButton,
        DeviceSelector,
        DeviceDropdownItem,
        FrequencyColorToggle,
        HeightPresetSmall,
        HeightPresetMedium,
        HeightPresetLarge,
        AnalysisToggle,
        KeyProfileSelector,
        KeyProfileDropdownItem,
        ClearCacheButton,
        CheckUpdatesButton,
        ResetDefaultsButton,
        Background  // inside overlay but no interactive element
    }

    /// <summary>
    /// Per-instance settings overlay layout used for hit testing.
    /// </summary>
    public sealed class SettingsOverlayLayout
    {
        public Rectangle OverlayBounds { get; set; } = Rectangle.Empty;
        public Rectangle CloseButtonRect { get; set; } = Rectangle.Empty;
        public Rectangle DeviceSelectorRect { get; set; } = Rectangle.Empty;
        public Rectangle FrequencyColorToggleRect { get; set; } = Rectangle.Empty;
        public Rectangle HeightSmallRect { get; set; } = Rectangle.Empty;
        public Rectangle HeightMediumRect { get; set; } = Rectangle.Empty;
        public Rectangle HeightLargeRect { get; set; } = Rectangle.Empty;
        public Rectangle AnalysisToggleRect { get; set; } = Rectangle.Empty;
        public Rectangle KeyProfileSelectorRect { get; set; } = Rectangle.Empty;
        public Rectangle ClearCacheButtonRect { get; set; } = Rectangle.Empty;
        public Rectangle CheckUpdatesButtonRect { get; set; } = Rectangle.Empty;
        public Rectangle ResetDefaultsButtonRect { get; set; } = Rectangle.Empty;
        public List<Rectangle> DeviceDropdownItemRects { get; } = new List<Rectangle>();
        public List<Rectangle> KeyProfileDropdownItemRects { get; } = new List<Rectangle>();

        public static SettingsOverlayLayout Empty => new SettingsOverlayLayout();
    }

    /// <summary>
    /// Static, owner-drawn GDI+ settings overlay panel.
    /// Follows the ControlBarRenderer pattern: static renderer, no owned state,
    /// cached layout rectangles for HitTest(), Draw() called from OnPaint.
    ///
    /// The overlay covers the right portion of the preview pane (DPI-scaled width),
    /// full height. All controls are GDI+ drawn — no WinForms child controls.
    /// </summary>
    public static class SettingsOverlayRenderer
    {
        // Overlay width in logical pixels at 96 DPI
        private const int OverlayWidthBase = 280;

        // Layout constants (logical pixels at 96 DPI)
        private const int PadBase = 12;
        private const int TitleHeightBase = 36;
        private const int SectionHeaderHeightBase = 22;
        private const int RowHeightBase = 28;
        private const int ToggleWidthBase = 38;
        private const int ToggleHeightBase = 18;
        private const int RadioRadiusBase = 6;
        private const int DividerHeightBase = 1;
        private const int CloseBtnSizeBase = 20;
        private const int DeviceSelectorHeightBase = 26;
        private const int DeviceDropdownItemHeightBase = 24;
        private const int ButtonHeightBase = 26;

        private static readonly (string Value, string Label)[] KeyProfileOptions =
        {
            ("auto", "Auto (best match)"),
            ("krumhansl", "Krumhansl"),
            ("temperley", "Temperley")
        };

        /// <summary>
        /// Calculates the overlay bounds from the full client rectangle.
        /// The overlay covers the right OverlayWidthBase logical pixels, full height.
        /// </summary>
        public static Rectangle GetOverlayBounds(Rectangle clientBounds, float dpiScale)
        {
            int overlayWidth = (int)(OverlayWidthBase * dpiScale);
            return new Rectangle(
                clientBounds.Right - overlayWidth,
                clientBounds.Top,
                overlayWidth,
                clientBounds.Height);
        }

        /// <summary>
        /// Draws the complete settings overlay.
        /// Call from OnPaint, AFTER all other rendering so overlay appears on top.
        /// </summary>
        /// <param name="g">Graphics from OnPaint</param>
        /// <param name="overlayBounds">Bounds of the overlay panel</param>
        /// <param name="config">Current AppConfig (read-only for rendering)</param>
        /// <param name="devices">WASAPI device list: (DeviceIndex, Name). Null if not yet enumerated.</param>
        /// <param name="isDarkMode">True for dark theme</param>
        /// <param name="selectedDeviceDropdownIndex">Index in devices list that is selected (-1 = none)</param>
        /// <param name="isDeviceDropdownOpen">True if the device dropdown is expanded</param>
        /// <param name="isKeyProfileDropdownOpen">True if the key profile dropdown is expanded</param>
        /// <param name="waveformHeightPreset">In-memory waveform height preset ("Small", "Medium", "Large")</param>
        public static SettingsOverlayLayout Draw(
            Graphics g,
            Rectangle overlayBounds,
            AppConfig config,
            List<(int Index, string Name)>? devices,
            bool isDarkMode,
            int selectedDeviceDropdownIndex,
            bool isDeviceDropdownOpen,
            bool isKeyProfileDropdownOpen,
            string waveformHeightPreset)
        {
            var layout = new SettingsOverlayLayout
            {
                OverlayBounds = overlayBounds
            };

            float dpiScale = g.DpiX / 96.0f;

            int pad = (int)(PadBase * dpiScale);
            int titleHeight = (int)(TitleHeightBase * dpiScale);
            int sectionHeaderHeight = (int)(SectionHeaderHeightBase * dpiScale);
            int rowHeight = (int)(RowHeightBase * dpiScale);
            int toggleWidth = (int)(ToggleWidthBase * dpiScale);
            int toggleHeight = (int)(ToggleHeightBase * dpiScale);
            int radioRadius = (int)(RadioRadiusBase * dpiScale);
            int closeBtnSize = (int)(CloseBtnSizeBase * dpiScale);
            int deviceSelectorHeight = (int)(DeviceSelectorHeightBase * dpiScale);
            int buttonHeight = (int)(ButtonHeightBase * dpiScale);

            Color bgColor = ThemeHelper.SettingsOverlayBackground(isDarkMode);
            Color textColor = ThemeHelper.SettingsOverlayText(isDarkMode);
            Color sectionColor = ThemeHelper.SettingsOverlaySectionHeader(isDarkMode);
            Color controlColor = ThemeHelper.SettingsOverlayControl(isDarkMode);
            Color activeColor = ThemeHelper.SettingsOverlayControlActive(isDarkMode);
            Color dividerColor = ThemeHelper.SettingsOverlayDivider(isDarkMode);

            var oldMode = g.SmoothingMode;

            // ---- Draw overlay background ----
            using (Brush bgBrush = new SolidBrush(bgColor))
            {
                g.FillRectangle(bgBrush, overlayBounds);
            }

            // Left border line
            using (Pen borderPen = new Pen(dividerColor, 1.0f))
            {
                g.DrawLine(borderPen,
                    overlayBounds.Left, overlayBounds.Top,
                    overlayBounds.Left, overlayBounds.Bottom);
            }

            int y = overlayBounds.Top;
            int x = overlayBounds.Left;
            int w = overlayBounds.Width;

            // ---- Title row ----
            // X close button in top-right
            layout.CloseButtonRect = new Rectangle(
                x + w - pad - closeBtnSize,
                y + (titleHeight - closeBtnSize) / 2,
                closeBtnSize, closeBtnSize);

            using (Font titleFont = new Font("Segoe UI", 11.0f * dpiScale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush titleBrush = new SolidBrush(textColor))
            {
                StringFormat sf = new StringFormat { LineAlignment = StringAlignment.Center };
                Rectangle titleRect = new Rectangle(x + pad, y, w - pad * 2 - closeBtnSize - pad, titleHeight);
                g.DrawString("Settings", titleFont, titleBrush, titleRect, sf);
            }

            // Draw X button
            DrawCloseButton(g, layout.CloseButtonRect, textColor, dpiScale);

            y += titleHeight;

            // Divider
            using (Pen divPen = new Pen(dividerColor, 1.0f))
            {
                g.DrawLine(divPen, x, y, x + w, y);
            }
            y += pad;

            // ---- OUTPUT DEVICE section ----
            DrawSectionHeader(g, x, ref y, w, pad, sectionHeaderHeight, "Output Device", sectionColor, dpiScale);

            // Device selector
            string deviceName = "Default Output Device";
            if (devices != null && selectedDeviceDropdownIndex >= 0 && selectedDeviceDropdownIndex < devices.Count)
            {
                deviceName = devices[selectedDeviceDropdownIndex].Name;
            }
            else if (config.WasapiDeviceIndex == -1)
            {
                deviceName = "Default Output Device";
            }

            layout.DeviceSelectorRect = new Rectangle(x + pad, y, w - pad * 2, deviceSelectorHeight);
            DrawDropdownSelector(g, layout.DeviceSelectorRect, deviceName, isDeviceDropdownOpen, controlColor, textColor, dividerColor, dpiScale);
            y += deviceSelectorHeight + (int)(4 * dpiScale);

            // Device dropdown items (if open)
            layout.DeviceDropdownItemRects.Clear();
            if (isDeviceDropdownOpen && devices != null)
            {
                int itemHeight = (int)(DeviceDropdownItemHeightBase * dpiScale);
                int dropTop = y;

                Color dropBg = isDarkMode
                    ? Color.FromArgb(255, 45, 45, 48)
                    : Color.FromArgb(255, 245, 245, 248);

                using (Brush dropBgBrush = new SolidBrush(dropBg))
                {
                    // Draw dropdown background behind all items
                    int totalDropHeight = itemHeight * devices.Count;
                    g.FillRectangle(dropBgBrush, x + pad, dropTop, w - pad * 2, totalDropHeight);
                }

                for (int i = 0; i < devices.Count; i++)
                {
                    Rectangle itemRect = new Rectangle(x + pad, dropTop + i * itemHeight, w - pad * 2, itemHeight);
                    layout.DeviceDropdownItemRects.Add(itemRect);

                    bool isSelected = (i == selectedDeviceDropdownIndex);
                    if (isSelected)
                    {
                        using (Brush selBrush = new SolidBrush(activeColor))
                        {
                            g.FillRectangle(selBrush, itemRect);
                        }
                    }

                    Color itemTextColor = isSelected ? Color.White : textColor;
                    using (Font itemFont = new Font("Segoe UI", 9.0f * dpiScale, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (Brush itemBrush = new SolidBrush(itemTextColor))
                    {
                        StringFormat sf = new StringFormat
                        {
                            LineAlignment = StringAlignment.Center,
                            Trimming = StringTrimming.EllipsisCharacter,
                            FormatFlags = StringFormatFlags.NoWrap
                        };
                        Rectangle textRect = new Rectangle(itemRect.X + (int)(6 * dpiScale), itemRect.Y,
                            itemRect.Width - (int)(12 * dpiScale), itemRect.Height);
                        g.DrawString(devices[i].Name, itemFont, itemBrush, textRect, sf);
                    }
                }
                y += itemHeight * devices.Count + (int)(4 * dpiScale);
            }

            // "Takes effect on next file" note
            using (Font noteFont = new Font("Segoe UI", 8.0f * dpiScale, FontStyle.Italic, GraphicsUnit.Pixel))
            using (Brush noteBrush = new SolidBrush(Color.FromArgb(130, textColor.R, textColor.G, textColor.B)))
            {
                StringFormat sf = new StringFormat { LineAlignment = StringAlignment.Near };
                g.DrawString("Takes effect on next file", noteFont, noteBrush,
                    new Rectangle(x + pad, y, w - pad * 2, (int)(16 * dpiScale)), sf);
            }
            y += (int)(18 * dpiScale);

            // Divider
            DrawDivider(g, x, ref y, w, pad, dividerColor);

            // ---- WAVEFORM section ----
            DrawSectionHeader(g, x, ref y, w, pad, sectionHeaderHeight, "Waveform", sectionColor, dpiScale);

            // Frequency coloring toggle
            DrawToggleRow(g, x, ref y, w, pad, rowHeight, toggleWidth, toggleHeight,
                "Frequency coloring", config.WaveformColorMode,
                textColor, controlColor, activeColor, dpiScale,
                out Rectangle freqColorToggleRect);
            layout.FrequencyColorToggleRect = freqColorToggleRect;

            // Height preset
            y += (int)(4 * dpiScale);
            DrawLabel(g, x + pad, y, "Height:", textColor, dpiScale);

            int radioY = y + (rowHeight - radioRadius * 2) / 2;
            int labelWidth = (int)(36 * dpiScale);
            int radioSpacing = (int)(60 * dpiScale);
            int radioStartX = x + pad + labelWidth + (int)(8 * dpiScale);

            layout.HeightSmallRect = new Rectangle(radioStartX, radioY, radioRadius * 2, radioRadius * 2);
            layout.HeightMediumRect = new Rectangle(radioStartX + radioSpacing, radioY, radioRadius * 2, radioRadius * 2);
            layout.HeightLargeRect = new Rectangle(radioStartX + radioSpacing * 2, radioY, radioRadius * 2, radioRadius * 2);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawRadioButton(g, layout.HeightSmallRect, waveformHeightPreset == "Small", controlColor, activeColor);
            DrawRadioButton(g, layout.HeightMediumRect, waveformHeightPreset == "Medium", controlColor, activeColor);
            DrawRadioButton(g, layout.HeightLargeRect, waveformHeightPreset == "Large", controlColor, activeColor);
            g.SmoothingMode = oldMode;

            using (Font radioFont = new Font("Segoe UI", 8.5f * dpiScale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush radioBrush = new SolidBrush(textColor))
            {
                int textY = y + (rowHeight - (int)(radioFont.GetHeight(g))) / 2;
                g.DrawString("S", radioFont, radioBrush, layout.HeightSmallRect.Right + (int)(3 * dpiScale), textY);
                g.DrawString("M", radioFont, radioBrush, layout.HeightMediumRect.Right + (int)(3 * dpiScale), textY);
                g.DrawString("L", radioFont, radioBrush, layout.HeightLargeRect.Right + (int)(3 * dpiScale), textY);
            }
            y += rowHeight;

            // Divider
            DrawDivider(g, x, ref y, w, pad, dividerColor);

            // ---- ANALYSIS section ----
            DrawSectionHeader(g, x, ref y, w, pad, sectionHeaderHeight, "Analysis", sectionColor, dpiScale);

            // Analysis toggle
            DrawToggleRow(g, x, ref y, w, pad, rowHeight, toggleWidth, toggleHeight,
                "Auto BPM/key detection", config.EnableBpmKeyDetection,
                textColor, controlColor, activeColor, dpiScale,
                out Rectangle analysisToggleRect);
            layout.AnalysisToggleRect = analysisToggleRect;

            // Key profile selector
            y += (int)(4 * dpiScale);
            DrawLabel(g, x + pad, y, "Key profile:", textColor, dpiScale);
            y += (int)(16 * dpiScale);

            string keyProfileValue = NormalizeKeyProfile(config.KeyDetectionProfile);
            string keyProfileLabel = GetKeyProfileLabel(keyProfileValue);
            layout.KeyProfileSelectorRect = new Rectangle(x + pad, y, w - pad * 2, deviceSelectorHeight);
            DrawDropdownSelector(g, layout.KeyProfileSelectorRect, keyProfileLabel, isKeyProfileDropdownOpen,
                controlColor, textColor, dividerColor, dpiScale);
            y += deviceSelectorHeight + (int)(4 * dpiScale);

            // Key profile dropdown items (if open)
            layout.KeyProfileDropdownItemRects.Clear();
            if (isKeyProfileDropdownOpen)
            {
                int itemHeight = (int)(DeviceDropdownItemHeightBase * dpiScale);
                int dropTop = y;

                Color dropBg = isDarkMode
                    ? Color.FromArgb(255, 45, 45, 48)
                    : Color.FromArgb(255, 245, 245, 248);

                using (Brush dropBgBrush = new SolidBrush(dropBg))
                {
                    int totalDropHeight = itemHeight * KeyProfileOptions.Length;
                    g.FillRectangle(dropBgBrush, x + pad, dropTop, w - pad * 2, totalDropHeight);
                }

                for (int i = 0; i < KeyProfileOptions.Length; i++)
                {
                    Rectangle itemRect = new Rectangle(x + pad, dropTop + i * itemHeight, w - pad * 2, itemHeight);
                    layout.KeyProfileDropdownItemRects.Add(itemRect);

                    bool isSelected = string.Equals(KeyProfileOptions[i].Value, keyProfileValue, StringComparison.Ordinal);
                    if (isSelected)
                    {
                        using (Brush selBrush = new SolidBrush(activeColor))
                        {
                            g.FillRectangle(selBrush, itemRect);
                        }
                    }

                    Color itemTextColor = isSelected ? Color.White : textColor;
                    using (Font itemFont = new Font("Segoe UI", 9.0f * dpiScale, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (Brush itemBrush = new SolidBrush(itemTextColor))
                    {
                        StringFormat sf = new StringFormat
                        {
                            LineAlignment = StringAlignment.Center,
                            Trimming = StringTrimming.EllipsisCharacter,
                            FormatFlags = StringFormatFlags.NoWrap
                        };
                        Rectangle textRect = new Rectangle(itemRect.X + (int)(6 * dpiScale), itemRect.Y,
                            itemRect.Width - (int)(12 * dpiScale), itemRect.Height);
                        g.DrawString(KeyProfileOptions[i].Label, itemFont, itemBrush, textRect, sf);
                    }
                }
                y += itemHeight * KeyProfileOptions.Length + (int)(4 * dpiScale);
            }

            // Clear analysis cache button
            layout.ClearCacheButtonRect = new Rectangle(x + pad, y, w - pad * 2, buttonHeight);
            DrawButton(g, layout.ClearCacheButtonRect, "Clear analysis cache", textColor, controlColor, dividerColor, dpiScale);
            y += buttonHeight + (int)(6 * dpiScale);

            // Divider
            DrawDivider(g, x, ref y, w, pad, dividerColor);

            // ---- ABOUT section ----
            DrawSectionHeader(g, x, ref y, w, pad, sectionHeaderHeight, "About", sectionColor, dpiScale);

            // Check for updates button
            layout.CheckUpdatesButtonRect = new Rectangle(x + pad, y, w - pad * 2, buttonHeight);
            DrawButton(g, layout.CheckUpdatesButtonRect, "Check for updates", textColor, controlColor, dividerColor, dpiScale);
            y += buttonHeight + (int)(6 * dpiScale);

            // Reset to defaults button
            layout.ResetDefaultsButtonRect = new Rectangle(x + pad, y, w - pad * 2, buttonHeight);
            DrawButton(g, layout.ResetDefaultsButtonRect, "Reset to defaults", textColor, controlColor, dividerColor, dpiScale);
            y += buttonHeight + pad;

            g.SmoothingMode = oldMode;
            return layout;
        }

        /// <summary>
        /// Hit-tests a point against cached layout rectangles.
        /// Returns the overlay zone the point falls within, or None if outside overlay.
        /// </summary>
        public static SettingsHitZone HitTest(SettingsOverlayLayout layout, Point p)
        {
            // If outside overlay bounds entirely, not our problem
            if (!layout.OverlayBounds.Contains(p))
                return SettingsHitZone.None;

            if (layout.CloseButtonRect.Contains(p))
                return SettingsHitZone.CloseButton;

            if (layout.DeviceSelectorRect.Contains(p))
                return SettingsHitZone.DeviceSelector;

            if (layout.FrequencyColorToggleRect.Contains(p))
                return SettingsHitZone.FrequencyColorToggle;

            if (layout.HeightSmallRect.Contains(p))
                return SettingsHitZone.HeightPresetSmall;

            if (layout.HeightMediumRect.Contains(p))
                return SettingsHitZone.HeightPresetMedium;

            if (layout.HeightLargeRect.Contains(p))
                return SettingsHitZone.HeightPresetLarge;

            if (layout.AnalysisToggleRect.Contains(p))
                return SettingsHitZone.AnalysisToggle;

            if (layout.KeyProfileSelectorRect.Contains(p))
                return SettingsHitZone.KeyProfileSelector;

            if (layout.ClearCacheButtonRect.Contains(p))
                return SettingsHitZone.ClearCacheButton;

            if (layout.CheckUpdatesButtonRect.Contains(p))
                return SettingsHitZone.CheckUpdatesButton;

            if (layout.ResetDefaultsButtonRect.Contains(p))
                return SettingsHitZone.ResetDefaultsButton;

            // Check dropdown items
            for (int i = 0; i < layout.DeviceDropdownItemRects.Count; i++)
            {
                if (layout.DeviceDropdownItemRects[i].Contains(p))
                    return SettingsHitZone.DeviceDropdownItem;
            }

            for (int i = 0; i < layout.KeyProfileDropdownItemRects.Count; i++)
            {
                if (layout.KeyProfileDropdownItemRects[i].Contains(p))
                    return SettingsHitZone.KeyProfileDropdownItem;
            }

            // Inside overlay but not on any control
            return SettingsHitZone.Background;
        }

        /// <summary>
        /// Returns which device dropdown item index was clicked, or -1 if none.
        /// </summary>
        public static int GetDeviceDropdownItemIndex(SettingsOverlayLayout layout, Point p)
        {
            for (int i = 0; i < layout.DeviceDropdownItemRects.Count; i++)
            {
                if (layout.DeviceDropdownItemRects[i].Contains(p))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Returns which key profile dropdown item index was clicked, or -1 if none.
        /// </summary>
        public static int GetKeyProfileDropdownItemIndex(SettingsOverlayLayout layout, Point p)
        {
            for (int i = 0; i < layout.KeyProfileDropdownItemRects.Count; i++)
            {
                if (layout.KeyProfileDropdownItemRects[i].Contains(p))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Returns key profile config value for a dropdown item index.
        /// </summary>
        public static string GetKeyProfileValueByIndex(int index)
        {
            if (index < 0 || index >= KeyProfileOptions.Length)
                return "auto";
            return KeyProfileOptions[index].Value;
        }

        // -------------------------------------------------------------------------
        // Private drawing helpers
        // -------------------------------------------------------------------------

        private static void DrawSectionHeader(Graphics g, int x, ref int y, int w, int pad,
            int sectionHeaderHeight, string title, Color color, float dpiScale)
        {
            using (Font hdrFont = new Font("Segoe UI", 8.5f * dpiScale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush hdrBrush = new SolidBrush(color))
            {
                StringFormat sf = new StringFormat { LineAlignment = StringAlignment.Center };
                g.DrawString(title.ToUpperInvariant(), hdrFont, hdrBrush,
                    new Rectangle(x + pad, y, w - pad * 2, sectionHeaderHeight), sf);
            }
            y += sectionHeaderHeight;
        }

        private static void DrawLabel(Graphics g, int x, int y, string text, Color color, float dpiScale)
        {
            using (Font f = new Font("Segoe UI", 9.0f * dpiScale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush b = new SolidBrush(color))
            {
                g.DrawString(text, f, b, x, y);
            }
        }

        private static void DrawDivider(Graphics g, int x, ref int y, int w, int pad, Color color)
        {
            y += pad / 2;
            using (Pen pen = new Pen(color, 1.0f))
            {
                g.DrawLine(pen, x + pad, y, x + w - pad, y);
            }
            y += 1 + pad / 2;
        }

        private static void DrawToggleRow(Graphics g, int x, ref int y, int w, int pad,
            int rowHeight, int toggleWidth, int toggleHeight,
            string label, bool isOn,
            Color textColor, Color controlColor, Color activeColor, float dpiScale,
            out Rectangle toggleRect)
        {
            // Toggle is on the right side of the row
            int toggleX = x + w - pad - toggleWidth;
            int toggleY = y + (rowHeight - toggleHeight) / 2;
            toggleRect = new Rectangle(toggleX, toggleY, toggleWidth, toggleHeight);

            // Label on the left
            using (Font f = new Font("Segoe UI", 9.0f * dpiScale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush b = new SolidBrush(textColor))
            {
                StringFormat sf = new StringFormat { LineAlignment = StringAlignment.Center };
                Rectangle labelRect = new Rectangle(x + pad, y, w - pad * 3 - toggleWidth, rowHeight);
                g.DrawString(label, f, b, labelRect, sf);
            }

            // Draw toggle switch
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawToggleSwitch(g, toggleRect, isOn, controlColor, activeColor);
            g.SmoothingMode = oldMode;

            y += rowHeight;
        }

        private static void DrawToggleSwitch(Graphics g, Rectangle rect, bool isOn, Color trackColor, Color activeColor)
        {
            int radius = rect.Height / 2;
            Color track = isOn ? activeColor : trackColor;

            // Draw rounded track
            using (GraphicsPath path = RoundedRectPath(rect, radius))
            using (Brush trackBrush = new SolidBrush(track))
            {
                g.FillPath(trackBrush, path);
            }

            // Draw thumb circle
            int thumbDiam = rect.Height - 4;
            int thumbX = isOn ? rect.Right - 2 - thumbDiam : rect.Left + 2;
            int thumbY = rect.Top + 2;
            using (Brush thumbBrush = new SolidBrush(Color.White))
            {
                g.FillEllipse(thumbBrush, thumbX, thumbY, thumbDiam, thumbDiam);
            }
        }

        private static void DrawDropdownSelector(Graphics g, Rectangle rect, string text,
            bool isOpen, Color controlColor, Color textColor, Color borderColor, float dpiScale)
        {
            // Background
            using (Brush bg = new SolidBrush(controlColor))
            {
                g.FillRectangle(bg, rect);
            }
            using (Pen border = new Pen(borderColor, 1.0f))
            {
                g.DrawRectangle(border, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            }

            // Text
            using (Font f = new Font("Segoe UI", 9.0f * dpiScale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush b = new SolidBrush(textColor))
            {
                StringFormat sf = new StringFormat
                {
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                };
                int arrowW = (int)(18 * dpiScale);
                Rectangle textRect = new Rectangle(rect.X + (int)(6 * dpiScale), rect.Y,
                    rect.Width - arrowW - (int)(6 * dpiScale), rect.Height);
                g.DrawString(text, f, b, textRect, sf);
            }

            // Dropdown arrow (chevron)
            int arrowSize = (int)(6 * dpiScale);
            int arrowX = rect.Right - (int)(14 * dpiScale);
            int arrowY = rect.Top + (rect.Height - arrowSize / 2) / 2;
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen arrowPen = new Pen(textColor, 1.5f))
            {
                if (isOpen)
                {
                    // Up chevron
                    g.DrawLine(arrowPen, arrowX, arrowY + arrowSize / 2, arrowX + arrowSize / 2, arrowY);
                    g.DrawLine(arrowPen, arrowX + arrowSize / 2, arrowY, arrowX + arrowSize, arrowY + arrowSize / 2);
                }
                else
                {
                    // Down chevron
                    g.DrawLine(arrowPen, arrowX, arrowY, arrowX + arrowSize / 2, arrowY + arrowSize / 2);
                    g.DrawLine(arrowPen, arrowX + arrowSize / 2, arrowY + arrowSize / 2, arrowX + arrowSize, arrowY);
                }
            }
            g.SmoothingMode = oldMode;
        }

        private static void DrawButton(Graphics g, Rectangle rect, string text,
            Color textColor, Color controlColor, Color borderColor, float dpiScale)
        {
            using (Brush bg = new SolidBrush(controlColor))
            {
                g.FillRectangle(bg, rect);
            }
            using (Pen border = new Pen(borderColor, 1.0f))
            {
                g.DrawRectangle(border, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            }
            using (Font f = new Font("Segoe UI", 9.0f * dpiScale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush b = new SolidBrush(textColor))
            {
                StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(text, f, b, rect, sf);
            }
        }

        private static void DrawRadioButton(Graphics g, Rectangle rect, bool isSelected,
            Color trackColor, Color activeColor)
        {
            using (Pen pen = new Pen(isSelected ? activeColor : trackColor, 1.5f))
            {
                g.DrawEllipse(pen, rect);
            }
            if (isSelected)
            {
                int innerPad = Math.Max(2, rect.Width / 4);
                Rectangle innerRect = new Rectangle(
                    rect.X + innerPad, rect.Y + innerPad,
                    rect.Width - innerPad * 2, rect.Height - innerPad * 2);
                using (Brush fill = new SolidBrush(activeColor))
                {
                    g.FillEllipse(fill, innerRect);
                }
            }
        }

        private static void DrawCloseButton(Graphics g, Rectangle rect, Color color, float dpiScale)
        {
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int pad = (int)(4 * dpiScale);
            using (Pen pen = new Pen(color, Math.Max(1.5f, 1.5f * dpiScale)))
            {
                g.DrawLine(pen,
                    rect.Left + pad, rect.Top + pad,
                    rect.Right - pad, rect.Bottom - pad);
                g.DrawLine(pen,
                    rect.Right - pad, rect.Top + pad,
                    rect.Left + pad, rect.Bottom - pad);
            }
            g.SmoothingMode = oldMode;
        }

        private static string NormalizeKeyProfile(string? profile)
        {
            if (string.IsNullOrWhiteSpace(profile))
                return "auto";

            string normalized = profile!.Trim().ToLowerInvariant();
            if (normalized == "krumhansl" || normalized == "temperley" || normalized == "auto")
                return normalized;

            return "auto";
        }

        private static string GetKeyProfileLabel(string profileValue)
        {
            for (int i = 0; i < KeyProfileOptions.Length; i++)
            {
                if (string.Equals(KeyProfileOptions[i].Value, profileValue, StringComparison.Ordinal))
                    return KeyProfileOptions[i].Label;
            }
            return KeyProfileOptions[0].Label;
        }

        private static GraphicsPath RoundedRectPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            int diam = radius * 2;
            path.AddArc(rect.Left, rect.Top, diam, diam, 180, 90);
            path.AddArc(rect.Right - diam, rect.Top, diam, diam, 270, 90);
            path.AddArc(rect.Right - diam, rect.Bottom - diam, diam, diam, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diam, diam, diam, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
