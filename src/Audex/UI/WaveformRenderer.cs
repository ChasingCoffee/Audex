using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Audex.UI
{
    /// <summary>
    /// Static, owner-drawn renderer for the waveform visualization area.
    /// Draws mirrored vertical bars with rounded tops, amplitude-based blue-to-cyan gradient,
    /// played-portion dimming, white playhead with triangle marker, hover guide line with
    /// time tooltip, and start/end time labels.
    ///
    /// Does NOT own state — receives state and renders it. State is managed by PreviewWindow.
    /// Layout bounds are cached in static fields so HitTest works without re-computing.
    /// Follows the ControlBarRenderer pattern exactly.
    /// </summary>
    public static class WaveformRenderer
    {
        /// <summary>
        /// Draws the waveform visualization area within the given bounds.
        /// </summary>
        /// <param name="g">Graphics context.</param>
        /// <param name="bounds">Full waveform area bounds.</param>
        /// <param name="peaks">Canonical peak array (up to 2000 entries), or null if not ready.</param>
        /// <param name="barsReady">Number of bars computed so far (for progressive reveal). Use peaks.Length for complete.</param>
        /// <param name="frequencyColors">Per-bar frequency colors, or null if not yet computed.</param>
        /// <param name="isColorMode">True to render using frequency colors when available.</param>
        /// <param name="currentPosition">Current playback position in seconds.</param>
        /// <param name="totalDuration">Total track duration in seconds.</param>
        /// <param name="dpiScale">DPI scale factor (g.DpiX / 96f).</param>
        /// <param name="isHovering">True if mouse is within the waveform area (but not over toggle button).</param>
        /// <param name="hoverPoint">Mouse cursor position in control coordinates.</param>
        /// <param name="isDragging">True if user is dragging the playhead.</param>
        /// <param name="dragPosition">Drag seek position in seconds (valid when isDragging is true).</param>
        /// <param name="waveformUnavailable">True to show "Waveform unavailable" message instead of bars.</param>
        /// <param name="isToggleHovered">True if the toggle button is being hovered.</param>
        /// <param name="isTogglePressed">True if the toggle button is being pressed.</param>
        public static Rectangle Draw(
            Graphics g,
            Rectangle bounds,
            float[]? peaks,
            int barsReady,
            Color[]? frequencyColors,
            bool isColorMode,
            double currentPosition,
            double totalDuration,
            float dpiScale,
            bool isHovering,
            Point hoverPoint,
            bool isDragging,
            double dragPosition,
            bool waveformUnavailable,
            bool isToggleHovered,
            bool isTogglePressed)
        {
            if (g == null) return Rectangle.Empty;
            Rectangle toggleButtonRect = Rectangle.Empty;

            // 1. Background
            using (Brush bgBrush = new SolidBrush(ThemeHelper.GetWaveformBackgroundColor()))
            {
                g.FillRectangle(bgBrush, bounds);
            }

            // 1px top border
            using (Pen borderPen = new Pen(ThemeHelper.GetBorderColor(), 1f))
            {
                g.DrawLine(borderPen, bounds.Left, bounds.Top, bounds.Right, bounds.Top);
            }

            // 2. Unavailable state
            if (waveformUnavailable && peaks == null)
            {
                using (Font font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point))
                using (Brush textBrush = new SolidBrush(ThemeHelper.GetSecondaryTextColor()))
                using (StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    g.DrawString("Waveform unavailable", font, textBrush, bounds, sf);
                }
                return Rectangle.Empty;
            }

            // 3. Layout constants (DPI-scaled)
            int padding = (int)(8 * dpiScale);

            // Waveform bar area
            int barAreaLeft = bounds.Left + padding;
            int barAreaRight = bounds.Right - padding;
            int barAreaTop = bounds.Top + 1; // account for top border
            int barAreaBottom = bounds.Bottom;
            int waveformWidth = barAreaRight - barAreaLeft;
            int waveformHeight = barAreaBottom - barAreaTop;

            if (waveformWidth <= 0 || waveformHeight <= 0)
                return Rectangle.Empty;

            // Bar geometry
            int barWidth = Math.Max(1, (int)(2.5f * dpiScale));
            int barGap = Math.Max(1, (int)(1.5f * dpiScale));

            // Compute bar count that fills width evenly
            int barCount = (waveformWidth + barGap) / (barWidth + barGap);
            if (barCount <= 0) barCount = 1;

            // Recompute barWidth to fill the available width exactly
            // barCount * barWidth + (barCount - 1) * barGap = waveformWidth
            // barWidth = (waveformWidth - (barCount - 1) * barGap) / barCount
            int adjustedBarWidth = (waveformWidth - (barCount - 1) * barGap) / barCount;
            if (adjustedBarWidth < 1) adjustedBarWidth = 1;

            int centerY = barAreaTop + waveformHeight / 2;
            int maxHalfHeight = waveformHeight / 2 - 1;
            if (maxHalfHeight < 1) maxHalfHeight = 1;

            int roundRadius = Math.Min(adjustedBarWidth / 2, Math.Max(1, (int)(1.5f * dpiScale)));

            // 4. Peak downsampling
            // If peaks is null but not waveformUnavailable, show empty bars (generation in progress)
            float[] displayPeaks = new float[barCount];
            int barsToShow = 0;
            float peaksPerBar = 1f; // default; overwritten below if peaks available

            if (peaks != null && peaks.Length > 0)
            {
                peaksPerBar = (float)peaks.Length / barCount;

                // How many display bars are "ready" given barsReady in the source array
                barsToShow = Math.Min(barCount, (int)Math.Ceiling(barsReady / peaksPerBar));

                for (int i = 0; i < barCount; i++)
                {
                    // Map display bar i to source peaks range
                    int srcStart = (int)(i * peaksPerBar);
                    int srcEnd = (int)((i + 1) * peaksPerBar);
                    srcEnd = Math.Min(srcEnd, peaks.Length);
                    srcStart = Math.Min(srcStart, peaks.Length - 1);

                    if (srcStart > srcEnd) srcEnd = srcStart + 1;
                    if (srcEnd > peaks.Length) srcEnd = peaks.Length;

                    float sum = 0f;
                    int count = 0;
                    for (int j = srcStart; j < srcEnd; j++)
                    {
                        sum += peaks[j];
                        count++;
                    }
                    displayPeaks[i] = count > 0 ? sum / count : 0f;
                }
            }

            // Compute playhead X position
            double playheadRatio = 0.0;
            if (totalDuration > 0)
            {
                double pos = isDragging ? dragPosition : currentPosition;
                playheadRatio = Math.Max(0.0, Math.Min(1.0, pos / totalDuration));
            }
            int playheadX = barAreaLeft + (int)(playheadRatio * waveformWidth);

            // 5. Draw bars
            var oldSmoothingMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (int i = 0; i < barCount; i++)
            {
                if (i >= barsToShow && peaks != null)
                    break; // Progressive reveal: only draw ready bars

                float amplitude = displayPeaks[i];
                int halfHeight = Math.Max(2, (int)(amplitude * maxHalfHeight));

                int barX = barAreaLeft + i * (adjustedBarWidth + barGap);
                int barTop = centerY - halfHeight;
                int barBottom = centerY + halfHeight;
                int barHeight = barBottom - barTop;

                // Determine if this bar is "played" (behind playhead)
                bool isPlayed = barX + adjustedBarWidth < playheadX;
                Color barColor;

                if (isColorMode && frequencyColors != null && peaks != null)
                {
                    // Frequency color mode: downsample colors using same source range mapping as peaks
                    int srcStart = (int)(i * peaksPerBar);
                    int srcEnd = Math.Min((int)((i + 1) * peaksPerBar), frequencyColors.Length);
                    srcStart = Math.Min(srcStart, Math.Max(0, frequencyColors.Length - 1));

                    // Average RGB across source bars mapped to this display bar
                    int rSum = 0, gSum = 0, bSum = 0, cnt = 0;
                    for (int j = srcStart; j < srcEnd && j < frequencyColors.Length; j++)
                    {
                        rSum += frequencyColors[j].R;
                        gSum += frequencyColors[j].G;
                        bSum += frequencyColors[j].B;
                        cnt++;
                    }
                    if (cnt > 0)
                        barColor = Color.FromArgb(rSum / cnt, gSum / cnt, bSum / cnt);
                    else
                        barColor = ThemeHelper.GetWaveformBarColor(amplitude);
                }
                else
                {
                    barColor = ThemeHelper.GetWaveformBarColor(amplitude);
                }

                // Apply played-bar alpha dimming (55% opacity / alpha=140)
                if (isPlayed)
                    barColor = Color.FromArgb(140, barColor.R, barColor.G, barColor.B);

                // Draw bar with rounded tops/bottoms if tall enough
                using (Brush barBrush = new SolidBrush(barColor))
                {
                    int diameter = roundRadius * 2;
                    if (barHeight > diameter && adjustedBarWidth > diameter)
                    {
                        // Rounded rectangle
                        using (GraphicsPath path = new GraphicsPath())
                        {
                            // Top-left arc
                            path.AddArc(barX, barTop, diameter, diameter, 180, 90);
                            // Top-right arc
                            path.AddArc(barX + adjustedBarWidth - diameter, barTop, diameter, diameter, 270, 90);
                            // Bottom-right arc
                            path.AddArc(barX + adjustedBarWidth - diameter, barBottom - diameter, diameter, diameter, 0, 90);
                            // Bottom-left arc
                            path.AddArc(barX, barBottom - diameter, diameter, diameter, 90, 90);
                            path.CloseFigure();
                            g.FillPath(barBrush, path);
                        }
                    }
                    else
                    {
                        g.FillRectangle(barBrush, barX, barTop, adjustedBarWidth, barHeight);
                    }
                }
            }

            g.SmoothingMode = oldSmoothingMode;

            // 6. Center line
            using (Pen centerPen = new Pen(ThemeHelper.GetWaveformCenterLineColor(), 1f))
            {
                g.DrawLine(centerPen, barAreaLeft, centerY, barAreaRight, centerY);
            }

            // 7. Playhead
            if (totalDuration > 0)
            {
                Color playheadColor = ThemeHelper.GetWaveformPlayheadColor();
                using (Pen playheadPen = new Pen(playheadColor, Math.Max(1f, 1.5f * dpiScale)))
                {
                    g.DrawLine(playheadPen, playheadX, barAreaTop, playheadX, barAreaBottom);
                }

                // Downward-pointing triangle at top of waveform area
                int triangleHalfBase = (int)(5 * dpiScale);
                int triangleHeight = (int)(10 * dpiScale);
                PointF[] triangle = new PointF[]
                {
                    new PointF(playheadX - triangleHalfBase, barAreaTop),
                    new PointF(playheadX + triangleHalfBase, barAreaTop),
                    new PointF(playheadX, barAreaTop + triangleHeight)
                };
                var savedSmoothingMode = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Brush triangleBrush = new SolidBrush(playheadColor))
                {
                    g.FillPolygon(triangleBrush, triangle);
                }
                g.SmoothingMode = savedSmoothingMode;
            }

            // 8. Hover guide line
            if (isHovering && !isDragging && bounds.Contains(hoverPoint))
            {
                using (Pen guidePen = new Pen(ThemeHelper.GetWaveformGuideLineColor(), 1f))
                {
                    g.DrawLine(guidePen, hoverPoint.X, barAreaTop, hoverPoint.X, barAreaBottom);
                }

                // 9. Hover time tooltip
                if (totalDuration > 0 && waveformWidth > 0)
                {
                    double hoverRatio = Math.Max(0.0, Math.Min(1.0,
                        (double)(hoverPoint.X - barAreaLeft) / waveformWidth));
                    double hoverTime = hoverRatio * totalDuration;
                    string timeText = LayoutRenderer.FormatDuration(hoverTime);

                    using (Font tipFont = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point))
                    {
                        SizeF textSize = g.MeasureString(timeText, tipFont);
                        int tipPadH = (int)(4 * dpiScale);
                        int tipPadV = (int)(2 * dpiScale);
                        int tipW = (int)textSize.Width + tipPadH * 2;
                        int tipH = (int)textSize.Height + tipPadV * 2;
                        int tipOffsetY = (int)(20 * dpiScale);
                        int tipY = hoverPoint.Y - tipOffsetY - tipH;
                        if (tipY < barAreaTop) tipY = hoverPoint.Y + (int)(4 * dpiScale);

                        int tipX = hoverPoint.X - tipW / 2;
                        // Clamp to waveform area
                        tipX = Math.Max(barAreaLeft, Math.Min(barAreaRight - tipW, tipX));

                        Color tipBg = ThemeHelper.IsSystemInDarkMode()
                            ? Color.FromArgb(200, 30, 30, 30)
                            : Color.FromArgb(200, 240, 240, 240);
                        Color tipText = ThemeHelper.IsSystemInDarkMode()
                            ? Color.FromArgb(255, 255, 255)
                            : Color.FromArgb(0, 0, 0);

                        using (Brush tipBgBrush = new SolidBrush(tipBg))
                        {
                            g.FillRectangle(tipBgBrush, tipX, tipY, tipW, tipH);
                        }
                        using (Brush tipTextBrush = new SolidBrush(tipText))
                        {
                            g.DrawString(timeText, tipFont, tipTextBrush,
                                new RectangleF(tipX + tipPadH, tipY + tipPadV, textSize.Width + 1, textSize.Height + 1));
                        }
                    }
                }
            }

            // 10. Toggle button (top-right corner of waveform area)
            int toggleSize = (int)(20 * dpiScale);
            int toggleMargin = (int)(6 * dpiScale);
            toggleButtonRect = new Rectangle(
                bounds.Right - toggleMargin - toggleSize,
                bounds.Top + toggleMargin + 1, // +1 for border
                toggleSize,
                toggleSize);

            // Background
            Color toggleBg = isTogglePressed ? ThemeHelper.GetToggleButtonPressColor()
                : isToggleHovered ? ThemeHelper.GetToggleButtonHoverColor()
                : ThemeHelper.GetToggleButtonBackground();
            using (Brush bgBrush = new SolidBrush(toggleBg))
            {
                g.FillRectangle(bgBrush, toggleButtonRect);
            }

            // Icon: 3 ascending rectangles for color mode, single bar for monochrome
            Color iconColor = ThemeHelper.GetToggleButtonIconColor();
            using (Brush iconBrush = new SolidBrush(iconColor))
            {
                int iconPad = (int)(4 * dpiScale);
                int iconLeft = toggleButtonRect.Left + iconPad;
                int iconBottom = toggleButtonRect.Bottom - iconPad;
                int iconWidth = toggleButtonRect.Width - iconPad * 2;
                int iconHeight = toggleButtonRect.Height - iconPad * 2;
                int barW = Math.Max(1, iconWidth / 5);
                int gap = Math.Max(1, barW / 2);

                if (isColorMode)
                {
                    // 3 ascending bars (spectrum icon) in frequency colors for visual feedback
                    Color bassHint = ThemeHelper.IsSystemInDarkMode() ? Color.FromArgb(255, 80, 15) : Color.FromArgb(210, 60, 10);
                    Color midsHint = ThemeHelper.IsSystemInDarkMode() ? Color.FromArgb(50, 255, 80) : Color.FromArgb(40, 210, 60);
                    Color highsHint = ThemeHelper.IsSystemInDarkMode() ? Color.FromArgb(40, 80, 255) : Color.FromArgb(30, 60, 210);

                    int h1 = iconHeight * 4 / 10;
                    int h2 = iconHeight * 7 / 10;
                    int h3 = iconHeight;

                    using (Brush b1 = new SolidBrush(bassHint))
                        g.FillRectangle(b1, iconLeft, iconBottom - h1, barW, h1);
                    using (Brush b2 = new SolidBrush(midsHint))
                        g.FillRectangle(b2, iconLeft + barW + gap, iconBottom - h2, barW, h2);
                    using (Brush b3 = new SolidBrush(highsHint))
                        g.FillRectangle(b3, iconLeft + 2 * (barW + gap), iconBottom - h3, barW, h3);
                }
                else
                {
                    // Single monochrome bar icon
                    int monoW = barW;
                    int monoH = iconHeight;
                    int monoX = toggleButtonRect.Left + (toggleButtonRect.Width - monoW) / 2;
                    g.FillRectangle(iconBrush, monoX, iconBottom - monoH, monoW, monoH);
                }
            }

            return toggleButtonRect;
        }

        /// <summary>
        /// Draws the waveform area showing a "Format Unavailable: {reason}" error message.
        /// Called by PreviewWindow.OnPaint when AudioFileInfo.FormatError is non-null.
        /// Uses the same background as the waveform area; text in secondary text color.
        /// </summary>
        public static void DrawFormatError(Graphics g, Rectangle bounds, string errorMessage, float dpiScale)
        {
            if (g == null) return;

            // Draw waveform background
            using (Brush bgBrush = new SolidBrush(ThemeHelper.GetWaveformBackgroundColor()))
            {
                g.FillRectangle(bgBrush, bounds);
            }

            // 1px top border (matches normal waveform appearance)
            using (Pen borderPen = new Pen(ThemeHelper.GetBorderColor(), 1f))
            {
                g.DrawLine(borderPen, bounds.Left, bounds.Top, bounds.Right, bounds.Top);
            }

            // Centered error message
            string displayText = errorMessage ?? "Format Unavailable";
            if (!displayText.StartsWith("Format Unavailable", StringComparison.OrdinalIgnoreCase))
                displayText = $"Format Unavailable: {displayText}";

            using (Font font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point))
            using (Brush textBrush = new SolidBrush(ThemeHelper.GetSecondaryTextColor()))
            using (StringFormat sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                int hPad = (int)(8 * dpiScale);
                RectangleF textBounds = new RectangleF(
                    bounds.Left + hPad, bounds.Top,
                    bounds.Width - hPad * 2, bounds.Height);
                g.DrawString(displayText, font, textBrush, textBounds, sf);
            }
        }

        /// <summary>
        /// Returns true if the point falls within the waveform bounds.
        /// Used by PreviewWindow for cursor and click handling.
        /// </summary>
        public static bool HitTest(Point point, Rectangle bounds)
        {
            return bounds.Contains(point);
        }

        /// <summary>
        /// Returns true if the point falls within the toggle button area.
        /// Must be checked BEFORE HitTest (waveform bounds) to prevent
        /// toggle clicks from also triggering waveform seeks.
        /// </summary>
        public static bool HitTestToggle(Point point, Rectangle toggleButtonRect)
        {
            return toggleButtonRect.Contains(point);
        }
    }
}
