using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Audex.Audio;

namespace Audex.UI
{
    /// <summary>
    /// Defines which zone of the control bar a point falls within.
    /// </summary>
    public enum HitZone
    {
        None,
        SeekBar,
        PlayPauseButton,
        StopButton,
        VolumeIcon,
        VolumeSlider,
        Waveform,
        AutoplayCheckbox,
        LoopCheckbox
    }

    /// <summary>
    /// Static, owner-drawn renderer for the audio player control bar.
    /// Draws seek bar (with time labels), transport buttons (Play/Pause/Stop),
    /// and volume slider (with speaker icon). All elements are DPI-scaled and theme-aware.
    ///
    /// Does NOT own state — receives state and renders it. State is managed by PreviewWindow.
    /// Layout rectangles are cached in static fields so HitTest works without re-computing.
    /// </summary>
    public static class ControlBarRenderer
    {
        /// <summary>
        /// Per-instance control bar layout used for hit testing.
        /// </summary>
        public readonly struct ControlBarLayout
        {
            public ControlBarLayout(
                Rectangle seekBarTrackRect,
                Rectangle playPauseButtonRect,
                Rectangle stopButtonRect,
                Rectangle volumeIconRect,
                Rectangle volumeSliderTrackRect,
                Rectangle autoplayCheckboxRect,
                Rectangle loopCheckboxRect)
            {
                SeekBarTrackRect = seekBarTrackRect;
                PlayPauseButtonRect = playPauseButtonRect;
                StopButtonRect = stopButtonRect;
                VolumeIconRect = volumeIconRect;
                VolumeSliderTrackRect = volumeSliderTrackRect;
                AutoplayCheckboxRect = autoplayCheckboxRect;
                LoopCheckboxRect = loopCheckboxRect;
            }

            public Rectangle SeekBarTrackRect { get; }
            public Rectangle PlayPauseButtonRect { get; }
            public Rectangle StopButtonRect { get; }
            public Rectangle VolumeIconRect { get; }
            public Rectangle VolumeSliderTrackRect { get; }
            public Rectangle AutoplayCheckboxRect { get; }
            public Rectangle LoopCheckboxRect { get; }

            public static ControlBarLayout Empty =>
                new ControlBarLayout(
                    Rectangle.Empty,
                    Rectangle.Empty,
                    Rectangle.Empty,
                    Rectangle.Empty,
                    Rectangle.Empty,
                    Rectangle.Empty,
                    Rectangle.Empty);
        }

        // Total height of the control bar in logical pixels at 96 DPI
        private const int ControlBarHeightBase = 60;

        // Seek bar row height (top portion of control bar)
        private const int SeekBarRowHeightBase = 28;

        // Button size (square)
        private const int ButtonSizeBase = 24;

        // Volume slider width
        private const int VolumeSliderWidthBase = 80;

        // Seek bar track height
        private const int SeekTrackHeightBase = 4;

        // Volume track height
        private const int VolumeTrackHeightBase = 4;

        // Seek bar thumb diameter
        private const int SeekThumbDiamBase = 10;

        // Volume thumb diameter
        private const int VolumeThumbDiamBase = 8;

        // Time label column width (approximate, fixed)
        private const int TimeLabelWidthBase = 36;

        // Horizontal padding inside control bar
        private const int PaddingBase = 8;

        // Checkbox size (square) for autoplay/loop controls
        private const int CheckboxSizeBase = 12;

        // Checkbox label font size
        private const float CheckboxFontSize = 7.5f;

        /// <summary>
        /// Returns the total height of the control bar in pixels, scaled by dpiScale.
        /// </summary>
        public static int GetControlBarHeight(float dpiScale)
        {
            return (int)(ControlBarHeightBase * dpiScale);
        }

        /// <summary>
        /// Draws the entire control bar within the given bounds.
        /// Caches layout rectangles for subsequent HitTest calls.
        /// </summary>
        public static ControlBarLayout Draw(
            Graphics g,
            Rectangle controlBarBounds,
            AudioPlayerState playerState,
            double currentPosition,
            double totalDuration,
            float volume,
            bool isMuted,
            HitZone hoveredZone,
            HitZone pressedZone,
            float dpiScale,
            bool isAutoplay = false,
            bool isLoop = false)
        {
            int pad = (int)(PaddingBase * dpiScale);
            int seekRowHeight = (int)(SeekBarRowHeightBase * dpiScale);
            int buttonSize = (int)(ButtonSizeBase * dpiScale);
            int volSliderWidth = (int)(VolumeSliderWidthBase * dpiScale);
            int timeLabelWidth = (int)(TimeLabelWidthBase * dpiScale);

            Rectangle seekBarTrackRect = Rectangle.Empty;
            Rectangle playPauseButtonRect = Rectangle.Empty;
            Rectangle stopButtonRect = Rectangle.Empty;
            Rectangle volumeIconRect = Rectangle.Empty;
            Rectangle volumeSliderTrackRect = Rectangle.Empty;
            Rectangle autoplayCheckboxRect = Rectangle.Empty;
            Rectangle loopCheckboxRect = Rectangle.Empty;

            // Get colors
            Color bgColor = ThemeHelper.GetControlBarBackgroundColor();
            Color borderColor = ThemeHelper.GetBorderColor();
            Color textColor = ThemeHelper.GetTextColor();
            Color secondaryColor = ThemeHelper.GetSecondaryTextColor();

            // Fill background
            using (Brush bgBrush = new SolidBrush(bgColor))
            {
                g.FillRectangle(bgBrush, controlBarBounds);
            }

            // Top separator line
            using (Pen borderPen = new Pen(borderColor, 1.0f))
            {
                g.DrawLine(borderPen, controlBarBounds.Left, controlBarBounds.Top,
                    controlBarBounds.Right, controlBarBounds.Top);
            }

            // ---- Row 1: Seek bar with time labels ----
            int seekRowY = controlBarBounds.Top + pad / 2;

            // Left time label
            string elapsedText = LayoutRenderer.FormatDuration(currentPosition);
            string totalText = totalDuration > 0 ? LayoutRenderer.FormatDuration(totalDuration) : "--:--";

            using (Font timeFont = new Font("Segoe UI", 8.0f, FontStyle.Regular, GraphicsUnit.Point))
            {
                SizeF elapsedSize = g.MeasureString(elapsedText, timeFont);
                SizeF totalSize = g.MeasureString(totalText, timeFont);
                int timeLabelW = Math.Max(timeLabelWidth, (int)Math.Max(elapsedSize.Width, totalSize.Width) + 2);

                int timeLabelY = seekRowY + (seekRowHeight - (int)elapsedSize.Height) / 2;

                using (Brush secondaryBrush = new SolidBrush(secondaryColor))
                using (StringFormat sfNear = new StringFormat { Alignment = StringAlignment.Near })
                using (StringFormat sfFar = new StringFormat { Alignment = StringAlignment.Far })
                {
                    // Elapsed time on left
                    g.DrawString(elapsedText, timeFont, secondaryBrush,
                        new RectangleF(controlBarBounds.Left + pad, timeLabelY, timeLabelW, seekRowHeight),
                        sfNear);

                    // Total time on right
                    g.DrawString(totalText, timeFont, secondaryBrush,
                        new RectangleF(controlBarBounds.Right - pad - timeLabelW, timeLabelY, timeLabelW, seekRowHeight),
                        sfFar);
                }

                // Seek bar track in the center
                int seekTrackHeight = (int)(SeekTrackHeightBase * dpiScale);
                int seekLeft = controlBarBounds.Left + pad + timeLabelW + pad;
                int seekRight = controlBarBounds.Right - pad - timeLabelW - pad;
                int seekWidth = seekRight - seekLeft;
                int seekTrackY = seekRowY + (seekRowHeight - seekTrackHeight) / 2;

                seekBarTrackRect = new Rectangle(seekLeft, seekRowY, seekWidth, seekRowHeight);

                if (seekWidth > 0)
                {
                    DrawSeekBar(g, seekLeft, seekTrackY, seekWidth, seekTrackHeight,
                        currentPosition, totalDuration, dpiScale,
                        hoveredZone == HitZone.SeekBar || pressedZone == HitZone.SeekBar);
                }
            }

            // ---- Row 2: Checkboxes + Transport buttons + volume slider ----
            int buttonsRowY = controlBarBounds.Top + seekRowHeight + pad / 2;
            int buttonsRowHeight = controlBarBounds.Bottom - buttonsRowY - pad / 2;
            int buttonY = buttonsRowY + (buttonsRowHeight - buttonSize) / 2;

            // Checkbox dimensions (DPI-scaled)
            int checkboxSize = (int)(CheckboxSizeBase * dpiScale);
            int checkboxY = buttonsRowY + (buttonsRowHeight - checkboxSize) / 2;

            // Checkbox label widths — measure approximate label widths
            // "Autoplay" label to the right of autoplay checkbox, "Loop" to the right of loop checkbox
            int checkboxLabelGap = (int)(3 * dpiScale);    // gap between checkbox and label
            int checkboxInterGap = (int)(8 * dpiScale);    // gap between Loop label and transport buttons

            // Measure label widths using an approximate font metrics approach
            // Font is CheckboxFontSize pt — estimated ~40px for "Autoplay", ~18px for "Loop" at 96dpi
            int autoLabelWidth = (int)(40 * dpiScale);
            int loopLabelWidth = (int)(18 * dpiScale);

            // Layout: [pad] [Auto checkbox] [checkboxLabelGap] [Auto label] [pad] [Loop checkbox] [checkboxLabelGap] [Loop label] [checkboxInterGap] | transport
            int autoCheckboxLeft = controlBarBounds.Left + pad;
            autoplayCheckboxRect = new Rectangle(autoCheckboxLeft, checkboxY, checkboxSize, checkboxSize);

            int autoLabelLeft = autoCheckboxLeft + checkboxSize + checkboxLabelGap;

            int loopCheckboxLeft = autoLabelLeft + autoLabelWidth + pad;
            loopCheckboxRect = new Rectangle(loopCheckboxLeft, checkboxY, checkboxSize, checkboxSize);

            int loopLabelLeft = loopCheckboxLeft + checkboxSize + checkboxLabelGap;

            int checkboxAreaRight = loopLabelLeft + loopLabelWidth + checkboxInterGap;

            // Volume area on the right: icon + slider
            int volIconSize = (int)(ButtonSizeBase * dpiScale);
            int volAreaWidth = volIconSize + pad / 2 + volSliderWidth;
            int volAreaLeft = controlBarBounds.Right - pad - volAreaWidth;

            // Speaker icon rect
            volumeIconRect = new Rectangle(volAreaLeft, buttonY, volIconSize, buttonSize);

            // Volume slider track rect
            int volSliderLeft = volAreaLeft + volIconSize + pad / 2;
            int volTrackHeight = (int)(VolumeTrackHeightBase * dpiScale);
            volumeSliderTrackRect = new Rectangle(volSliderLeft, buttonsRowY, volSliderWidth, buttonsRowHeight);

            // Transport buttons centered in remaining space (between checkboxes and volume)
            int transportAreaRight = volAreaLeft - pad;
            int transportAreaWidth = transportAreaRight - checkboxAreaRight;
            int totalButtonsWidth = buttonSize * 2 + pad;
            int transportLeft = checkboxAreaRight + (transportAreaWidth - totalButtonsWidth) / 2;
            if (transportLeft < checkboxAreaRight)
                transportLeft = checkboxAreaRight;

            // Play/Pause button
            playPauseButtonRect = new Rectangle(transportLeft, buttonY, buttonSize, buttonSize);

            // Stop button
            stopButtonRect = new Rectangle(transportLeft + buttonSize + pad, buttonY, buttonSize, buttonSize);

            // Draw autoplay and loop checkboxes
            DrawCheckbox(g, autoplayCheckboxRect, isAutoplay,
                hoveredZone == HitZone.AutoplayCheckbox, pressedZone == HitZone.AutoplayCheckbox, dpiScale);
            DrawCheckbox(g, loopCheckboxRect, isLoop,
                hoveredZone == HitZone.LoopCheckbox, pressedZone == HitZone.LoopCheckbox, dpiScale);

            // Draw checkbox labels
            using (Font checkFont = new Font("Segoe UI", CheckboxFontSize, FontStyle.Regular, GraphicsUnit.Point))
            using (Brush secondaryBrush = new SolidBrush(secondaryColor))
            {
                int labelY = buttonsRowY + (buttonsRowHeight - (int)(checkFont.GetHeight(g))) / 2;
                g.DrawString("Autoplay", checkFont, secondaryBrush, autoLabelLeft, labelY);
                g.DrawString("Loop", checkFont, secondaryBrush, loopLabelLeft, labelY);
            }

            // Draw transport buttons
            bool isPlaying = playerState == AudioPlayerState.Playing;
            DrawTransportButton(g, playPauseButtonRect, isPlaying ? ButtonType.Pause : ButtonType.Play,
                hoveredZone == HitZone.PlayPauseButton, pressedZone == HitZone.PlayPauseButton, dpiScale);

            DrawTransportButton(g, stopButtonRect, ButtonType.Stop,
                hoveredZone == HitZone.StopButton, pressedZone == HitZone.StopButton, dpiScale);

            // Draw volume speaker icon
            DrawSpeakerIcon(g, volumeIconRect, isMuted,
                hoveredZone == HitZone.VolumeIcon, pressedZone == HitZone.VolumeIcon, dpiScale);

            // Draw volume slider
            int volTrackY = buttonsRowY + (buttonsRowHeight - volTrackHeight) / 2;
            int volThumbDiam = (int)(VolumeThumbDiamBase * dpiScale);
            DrawVolumeSlider(g, volSliderLeft, volTrackY, volSliderWidth, volTrackHeight,
                volume, isMuted, volThumbDiam,
                hoveredZone == HitZone.VolumeSlider || pressedZone == HitZone.VolumeSlider, dpiScale);

            return new ControlBarLayout(
                seekBarTrackRect,
                playPauseButtonRect,
                stopButtonRect,
                volumeIconRect,
                volumeSliderTrackRect,
                autoplayCheckboxRect,
                loopCheckboxRect);
        }

        /// <summary>
        /// Returns the tooltip text for the given control bar hit zone.
        /// Shortcut-enabled controls include a "(click preview pane first)" note
        /// so users know to focus the pane before using keyboard shortcuts.
        /// Returns null for zones with no tooltip.
        /// </summary>
        public static string? GetTooltipText(HitZone zone)
        {
            switch (zone)
            {
                case HitZone.PlayPauseButton:
                    return "Play/Pause (Ctrl+Space -- click preview pane first)";
                case HitZone.StopButton:
                    return "Stop";
                case HitZone.VolumeIcon:
                    return "Mute/Unmute (Ctrl+M -- click preview pane first)";
                case HitZone.VolumeSlider:
                    return "Volume (Ctrl+Up/Down -- click preview pane first)";
                case HitZone.AutoplayCheckbox:
                    return "Auto-play on file select";
                case HitZone.LoopCheckbox:
                    return "Loop playback (Ctrl+L -- click preview pane first)";
                case HitZone.SeekBar:
                    return "Seek (Ctrl+Left/Right -- click preview pane first)";
                case HitZone.Waveform:
                    return "Click to seek";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Hit-tests a point against the cached control bar layout rectangles.
        /// Returns the zone containing the point, or HitZone.None.
        /// </summary>
        public static HitZone HitTest(Point point, in ControlBarLayout layout, float dpiScale)
        {
            // Check seek bar (full row height for easier clicking)
            if (layout.SeekBarTrackRect.Contains(point))
                return HitZone.SeekBar;

            // Check autoplay checkbox (expand hit target slightly for easier clicking)
            if (layout.AutoplayCheckboxRect != Rectangle.Empty)
            {
                int expand = (int)(4 * dpiScale);
                Rectangle autoExpanded = new Rectangle(
                    layout.AutoplayCheckboxRect.X - expand,
                    layout.AutoplayCheckboxRect.Y - expand,
                    layout.AutoplayCheckboxRect.Width + expand * 2,
                    layout.AutoplayCheckboxRect.Height + expand * 2);
                if (autoExpanded.Contains(point))
                    return HitZone.AutoplayCheckbox;
            }

            // Check loop checkbox (expand hit target slightly for easier clicking)
            if (layout.LoopCheckboxRect != Rectangle.Empty)
            {
                int expand = (int)(4 * dpiScale);
                Rectangle loopExpanded = new Rectangle(
                    layout.LoopCheckboxRect.X - expand,
                    layout.LoopCheckboxRect.Y - expand,
                    layout.LoopCheckboxRect.Width + expand * 2,
                    layout.LoopCheckboxRect.Height + expand * 2);
                if (loopExpanded.Contains(point))
                    return HitZone.LoopCheckbox;
            }

            // Check play/pause button
            if (layout.PlayPauseButtonRect.Contains(point))
                return HitZone.PlayPauseButton;

            // Check stop button
            if (layout.StopButtonRect.Contains(point))
                return HitZone.StopButton;

            // Check volume icon
            if (layout.VolumeIconRect.Contains(point))
                return HitZone.VolumeIcon;

            // Check volume slider
            if (layout.VolumeSliderTrackRect.Contains(point))
                return HitZone.VolumeSlider;

            return HitZone.None;
        }

        // -------------------------------------------------------------------------
        // Private drawing helpers
        // -------------------------------------------------------------------------

        private enum ButtonType { Play, Pause, Stop }

        private static void DrawSeekBar(Graphics g, int x, int trackY, int width, int trackHeight,
            double currentPosition, double totalDuration, float dpiScale, bool highlighted)
        {
            Color trackColor = ThemeHelper.GetSeekBarTrackColor();
            Color fillColor = ThemeHelper.GetSeekBarFillColor();
            Color thumbColor = ThemeHelper.GetSeekBarThumbColor();
            int thumbDiam = (int)(SeekThumbDiamBase * dpiScale);

            double fillRatio = (totalDuration > 0 && currentPosition >= 0)
                ? Math.Max(0, Math.Min(1, currentPosition / totalDuration))
                : 0;

            int fillWidth = (int)(width * fillRatio);

            var oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Draw track background (rounded)
            using (Brush trackBrush = new SolidBrush(trackColor))
            {
                DrawRoundedRect(g, trackBrush, x, trackY, width, trackHeight, trackHeight / 2);
            }

            // Draw fill (elapsed)
            if (fillWidth > 0)
            {
                using (Brush fillBrush = new SolidBrush(fillColor))
                {
                    DrawRoundedRect(g, fillBrush, x, trackY, fillWidth, trackHeight, trackHeight / 2);
                }
            }

            // Draw thumb circle (shown when highlighted or has fill)
            if (highlighted || fillRatio > 0)
            {
                int thumbX = x + fillWidth - thumbDiam / 2;
                int thumbY = trackY + trackHeight / 2 - thumbDiam / 2;
                using (Brush thumbBrush = new SolidBrush(thumbColor))
                {
                    g.FillEllipse(thumbBrush, thumbX, thumbY, thumbDiam, thumbDiam);
                }
            }

            g.SmoothingMode = oldMode;
        }

        private static void DrawVolumeSlider(Graphics g, int x, int trackY, int width, int trackHeight,
            float volume, bool isMuted, int thumbDiam, bool highlighted, float dpiScale)
        {
            Color trackColor = ThemeHelper.GetVolumeTrackColor();
            Color fillColor = ThemeHelper.GetVolumeFillColor();
            Color thumbColor = ThemeHelper.GetSeekBarThumbColor();

            float effectiveVolume = isMuted ? 0f : volume;
            int fillWidth = (int)(width * Math.Max(0, Math.Min(1, effectiveVolume)));

            var oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Track background
            using (Brush trackBrush = new SolidBrush(trackColor))
            {
                DrawRoundedRect(g, trackBrush, x, trackY, width, trackHeight, trackHeight / 2);
            }

            // Fill
            if (fillWidth > 0)
            {
                using (Brush fillBrush = new SolidBrush(fillColor))
                {
                    DrawRoundedRect(g, fillBrush, x, trackY, fillWidth, trackHeight, trackHeight / 2);
                }
            }

            // Thumb
            if (highlighted)
            {
                int thumbX = x + fillWidth - thumbDiam / 2;
                int thumbY = trackY + trackHeight / 2 - thumbDiam / 2;
                using (Brush thumbBrush = new SolidBrush(thumbColor))
                {
                    g.FillEllipse(thumbBrush, thumbX, thumbY, thumbDiam, thumbDiam);
                }
            }

            g.SmoothingMode = oldMode;
        }

        private static void DrawTransportButton(Graphics g, Rectangle rect, ButtonType type,
            bool hovered, bool pressed, float dpiScale)
        {
            // Draw button background for hover/press
            if (pressed)
            {
                using (Brush pressBrush = new SolidBrush(ThemeHelper.GetButtonPressColor()))
                {
                    g.FillRectangle(pressBrush, rect);
                }
            }
            else if (hovered)
            {
                using (Brush hoverBrush = new SolidBrush(ThemeHelper.GetButtonHoverColor()))
                {
                    g.FillRectangle(hoverBrush, rect);
                }
            }

            Color iconColor = ThemeHelper.GetButtonColor();
            int iconPad = (int)(5 * dpiScale);
            Rectangle iconRect = new Rectangle(
                rect.X + iconPad, rect.Y + iconPad,
                rect.Width - iconPad * 2, rect.Height - iconPad * 2);

            var oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            switch (type)
            {
                case ButtonType.Play:
                    DrawPlayIcon(g, iconRect, iconColor);
                    break;
                case ButtonType.Pause:
                    DrawPauseIcon(g, iconRect, iconColor);
                    break;
                case ButtonType.Stop:
                    DrawStopIcon(g, iconRect, iconColor);
                    break;
            }

            g.SmoothingMode = oldMode;
        }

        private static void DrawPlayIcon(Graphics g, Rectangle rect, Color color)
        {
            // Right-pointing filled triangle
            PointF[] triangle = new PointF[]
            {
                new PointF(rect.Left, rect.Top),
                new PointF(rect.Right, rect.Top + rect.Height / 2.0f),
                new PointF(rect.Left, rect.Bottom)
            };
            using (Brush brush = new SolidBrush(color))
            {
                g.FillPolygon(brush, triangle);
            }
        }

        private static void DrawPauseIcon(Graphics g, Rectangle rect, Color color)
        {
            // Two vertical rectangles
            int barWidth = Math.Max(2, rect.Width / 3);
            int gap = Math.Max(1, rect.Width / 5);
            int totalWidth = barWidth * 2 + gap;
            int startX = rect.Left + (rect.Width - totalWidth) / 2;

            using (Brush brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, startX, rect.Top, barWidth, rect.Height);
                g.FillRectangle(brush, startX + barWidth + gap, rect.Top, barWidth, rect.Height);
            }
        }

        private static void DrawStopIcon(Graphics g, Rectangle rect, Color color)
        {
            // Filled square
            using (Brush brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, rect);
            }
        }

        private static void DrawSpeakerIcon(Graphics g, Rectangle rect, bool muted,
            bool hovered, bool pressed, float dpiScale)
        {
            // Draw hover/press background
            if (pressed)
            {
                using (Brush pressBrush = new SolidBrush(ThemeHelper.GetButtonPressColor()))
                {
                    g.FillRectangle(pressBrush, rect);
                }
            }
            else if (hovered)
            {
                using (Brush hoverBrush = new SolidBrush(ThemeHelper.GetButtonHoverColor()))
                {
                    g.FillRectangle(hoverBrush, rect);
                }
            }

            int iconPad = (int)(5 * dpiScale);
            Rectangle iconRect = new Rectangle(
                rect.X + iconPad, rect.Y + iconPad,
                rect.Width - iconPad * 2, rect.Height - iconPad * 2);

            Color iconColor = ThemeHelper.GetButtonColor();
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Speaker body: rectangle (left ~40% of icon width) + triangle pointing right
            int bodyWidth = iconRect.Width * 2 / 5;
            int bodyHeight = iconRect.Height / 2;
            int bodyTop = iconRect.Top + (iconRect.Height - bodyHeight) / 2;
            Rectangle bodyRect = new Rectangle(iconRect.Left, bodyTop, bodyWidth, bodyHeight);

            using (Brush brush = new SolidBrush(iconColor))
            {
                // Speaker body rectangle
                g.FillRectangle(brush, bodyRect);

                // Speaker cone: triangle from right edge of body to full icon height
                PointF[] cone = new PointF[]
                {
                    new PointF(bodyRect.Right, bodyRect.Top),
                    new PointF(iconRect.Right - (muted ? iconRect.Width / 3 : 0), iconRect.Top),
                    new PointF(iconRect.Right - (muted ? iconRect.Width / 3 : 0), iconRect.Bottom),
                    new PointF(bodyRect.Right, bodyRect.Bottom)
                };
                g.FillPolygon(brush, cone);
            }

            // If not muted, draw sound waves (2 arcs)
            if (!muted)
            {
                int arcX = iconRect.Right - iconRect.Width / 3;
                using (Pen arcPen = new Pen(iconColor, Math.Max(1.0f, 1.5f * dpiScale)))
                {
                    int arc1Size = iconRect.Height / 3;
                    int arc2Size = iconRect.Height * 2 / 3;

                    g.DrawArc(arcPen,
                        arcX, iconRect.Top + (iconRect.Height - arc1Size) / 2,
                        arc1Size, arc1Size, -60, 120);

                    g.DrawArc(arcPen,
                        arcX - arc2Size / 4, iconRect.Top + (iconRect.Height - arc2Size) / 2,
                        arc2Size, arc2Size, -60, 120);
                }
            }
            else
            {
                // Draw X over speaker
                using (Pen xPen = new Pen(iconColor, Math.Max(1.5f, 2.0f * dpiScale)))
                {
                    int xArea = iconRect.Width / 3;
                    int xLeft = iconRect.Right - xArea;
                    g.DrawLine(xPen, xLeft, iconRect.Top, iconRect.Right, iconRect.Bottom);
                    g.DrawLine(xPen, iconRect.Right, iconRect.Top, xLeft, iconRect.Bottom);
                }
            }

            g.SmoothingMode = oldMode;
        }

        private static void DrawCheckbox(Graphics g, Rectangle rect, bool isChecked,
            bool hovered, bool pressed, float dpiScale)
        {
            Color borderColor = ThemeHelper.GetBorderColor();
            Color fillColor = ThemeHelper.GetSeekBarFillColor(); // accent color for checkmark fill
            Color textColor = ThemeHelper.GetTextColor();

            // Draw hover background
            if (pressed || hovered)
            {
                using (Brush hoverBrush = new SolidBrush(pressed
                    ? ThemeHelper.GetButtonPressColor()
                    : ThemeHelper.GetButtonHoverColor()))
                {
                    int hoverPad = (int)(3 * dpiScale);
                    g.FillRectangle(hoverBrush,
                        rect.X - hoverPad, rect.Y - hoverPad,
                        rect.Width + hoverPad * 2, rect.Height + hoverPad * 2);
                }
            }

            var oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (isChecked)
            {
                // Filled background when checked
                using (Brush fillBrush = new SolidBrush(fillColor))
                {
                    g.FillRectangle(fillBrush, rect);
                }

                // Draw checkmark (two lines forming a tick)
                using (Pen checkPen = new Pen(Color.White, Math.Max(1.5f, 1.5f * dpiScale)))
                {
                    float cx = rect.Left;
                    float cy = rect.Top;
                    float w = rect.Width;
                    float h = rect.Height;
                    // Tick: from (15%, 50%) to (40%, 80%) to (85%, 20%)
                    PointF p1 = new PointF(cx + w * 0.15f, cy + h * 0.50f);
                    PointF p2 = new PointF(cx + w * 0.40f, cy + h * 0.78f);
                    PointF p3 = new PointF(cx + w * 0.85f, cy + h * 0.22f);
                    g.DrawLine(checkPen, p1, p2);
                    g.DrawLine(checkPen, p2, p3);
                }

                // Border on top of fill
                using (Pen borderPen = new Pen(fillColor, 1.0f))
                {
                    g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                }
            }
            else
            {
                // Empty checkbox — just border
                using (Pen borderPen = new Pen(borderColor, 1.0f))
                {
                    g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                }
            }

            g.SmoothingMode = oldMode;
        }

        private static void DrawRoundedRect(Graphics g, Brush brush, int x, int y, int width, int height, int radius)
        {
            if (width <= 0 || height <= 0) return;

            if (radius <= 0 || radius * 2 >= width || radius * 2 >= height)
            {
                g.FillRectangle(brush, x, y, width, height);
                return;
            }

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
                path.AddArc(x + width - radius * 2, y, radius * 2, radius * 2, 270, 90);
                path.AddArc(x + width - radius * 2, y + height - radius * 2, radius * 2, radius * 2, 0, 90);
                path.AddArc(x, y + height - radius * 2, radius * 2, radius * 2, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }
    }
}
