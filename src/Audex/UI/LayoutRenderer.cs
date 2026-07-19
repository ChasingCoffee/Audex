using System;
using System.Drawing;
using Audex.Audio;
using Audex.FileReader;
using Audex.Utils;

namespace Audex.UI
{
    /// <summary>
    /// Renders the audio preview metadata area above the control bar.
    /// Shows filename, file size, and a two-column grid of technical metadata + optional tags.
    /// </summary>
    public static class LayoutRenderer
    {
        /// <summary>
        /// Renders the metadata area. The control bar is rendered separately by ControlBarRenderer.
        /// </summary>
        /// <param name="g">Graphics context</param>
        /// <param name="bounds">Metadata area bounds (already excludes control bar height)</param>
        /// <param name="info">Audio file information</param>
        /// <param name="showError">Whether to show error banner</param>
        /// <param name="errorMessage">Error message (if showError is true)</param>
        /// <param name="analysisResult">Current analysis result, or null if not yet analyzed</param>
        /// <param name="isAnalyzing">True while background analysis is running</param>
        /// <param name="analysisProgress">Analysis progress 0.0-1.0</param>
        /// <param name="isReanalyzing">True during re-analysis (old values shown dimmed)</param>
        /// <param name="isReanalyzeHovered">True when re-analyze button is hovered</param>
        /// <param name="reanalyzeButtonBounds">Output re-analyze button bounds for instance-scoped hit testing</param>
        public static void Render(Graphics g, Rectangle bounds, AudioFileInfo info, bool showError, string? errorMessage,
            AnalysisResult? analysisResult = null, bool isAnalyzing = false,
            float analysisProgress = 0f, bool isReanalyzing = false,
            bool isReanalyzeHovered = false)
        {
            Render(g, bounds, info, showError, errorMessage, analysisResult,
                isAnalyzing, analysisProgress, isReanalyzing, isReanalyzeHovered,
                out _);
        }

        public static void Render(Graphics g, Rectangle bounds, AudioFileInfo info, bool showError, string? errorMessage,
            AnalysisResult? analysisResult, bool isAnalyzing,
            float analysisProgress, bool isReanalyzing,
            bool isReanalyzeHovered, out Rectangle reanalyzeButtonBounds)
        {
            reanalyzeButtonBounds = Rectangle.Empty;
            if (g == null || info == null) return;

            Color bgColor = ThemeHelper.GetBackgroundColor();
            Color textColor = ThemeHelper.GetTextColor();
            Color secondaryTextColor = ThemeHelper.GetSecondaryTextColor();

            float dpiScale = g.DpiX / 96.0f;
            int padding = (int)(8 * dpiScale);

            // Clear background
            using (Brush bgBrush = new SolidBrush(bgColor))
            {
                g.FillRectangle(bgBrush, bounds);
            }

            int yOffset = bounds.Y + padding;

            // Draw error panel if needed — replaces all content when shown
            if (showError && !string.IsNullOrEmpty(errorMessage))
            {
                ErrorBanner.Draw(g, bounds, errorMessage ?? "Unknown error", PathHelper.GetLogFilePath());
                return;
            }

            int availableWidth = bounds.Width - padding * 2;
            if (availableWidth < 50) return;

            // ---- Filename header ----
            string displayName = info.FileName;
            if (!string.IsNullOrEmpty(displayName) && displayName != "Unknown")
            {
                displayName = System.IO.Path.GetFileNameWithoutExtension(displayName);
            }

            using (Font filenameFont = new Font("Segoe UI", 11.0f, FontStyle.Bold, GraphicsUnit.Point))
            using (Brush textBrush = new SolidBrush(textColor))
            using (StringFormat format = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                LineAlignment = StringAlignment.Near,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                float scaledFontHeight = filenameFont.GetHeight(g);
                float lineH = scaledFontHeight * 1.4f;
                g.DrawString(displayName, filenameFont, textBrush,
                    new RectangleF(bounds.X + padding, yOffset, availableWidth, lineH), format);
                yOffset += (int)lineH;
            }

            // ---- File size (secondary) ----
            using (Font bodyFont = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point))
            using (Brush secondaryBrush = new SolidBrush(secondaryTextColor))
            {
                float lineH = bodyFont.GetHeight(g) * 1.3f;
                g.DrawString(FormatFileSize(info.FileSize), bodyFont, secondaryBrush,
                    bounds.X + padding, yOffset);
                yOffset += (int)lineH + padding / 2;
            }

            // ---- Technical metadata grid ----
            yOffset = DrawMetadataGrid(g, bounds, info, yOffset, padding, dpiScale);

            // ---- Tag section (hidden if all tags are absent) ----
            bool hasTags = !string.IsNullOrEmpty(info.Title)
                        || !string.IsNullOrEmpty(info.Artist)
                        || !string.IsNullOrEmpty(info.Album);

            if (hasTags)
            {
                yOffset += padding / 2;
                yOffset = DrawTagGrid(g, bounds, info, yOffset, padding, dpiScale);
            }

            // ---- Music Info section (always visible; dashes for missing values) ----
            yOffset += padding / 2;
            DrawMusicInfoSection(g, bounds, info, yOffset, padding, dpiScale,
                analysisResult, isAnalyzing, analysisProgress, isReanalyzing, isReanalyzeHovered,
                ref reanalyzeButtonBounds);
        }

        /// <summary>
        /// Draws the two-column technical metadata grid (Format, Sample Rate, Bit Depth, Channels, Duration, Bitrate).
        /// Returns the updated y-offset after the grid.
        /// </summary>
        private static int DrawMetadataGrid(Graphics g, Rectangle bounds, AudioFileInfo info,
            int yOffset, int padding, float dpiScale)
        {
            Color textColor = ThemeHelper.GetTextColor();
            Color secondaryTextColor = ThemeHelper.GetSecondaryTextColor();

            int availableWidth = bounds.Width - padding * 2;
            int labelColWidth = (int)(availableWidth * 0.28f);
            int valueColX = bounds.X + padding + labelColWidth + (int)(4 * dpiScale);

            using (Font bodyFont = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point))
            using (Brush valueBrush = new SolidBrush(textColor))
            using (Brush labelBrush = new SolidBrush(secondaryTextColor))
            {
                float lineH = bodyFont.GetHeight(g) * 1.35f;
                int iLineH = (int)lineH;

                // Format
                if (!string.IsNullOrEmpty(info.Format))
                {
                    DrawGridRow(g, bodyFont, labelBrush, valueBrush,
                        bounds.X + padding, yOffset, labelColWidth, valueColX,
                        "Format", info.Format);
                    yOffset += iLineH;
                }

                if (info.ParseSucceeded && info.SampleRate > 0)
                {
                    // Sample Rate
                    DrawGridRow(g, bodyFont, labelBrush, valueBrush,
                        bounds.X + padding, yOffset, labelColWidth, valueColX,
                        "Sample Rate", $"{info.SampleRate:N0} Hz");
                    yOffset += iLineH;

                    // Bit Depth (skip if 0, or if module format — not meaningful for .mod/.xm/.it/.s3m)
                    if (info.BitDepth > 0 && !info.IsModuleFormat)
                    {
                        DrawGridRow(g, bodyFont, labelBrush, valueBrush,
                            bounds.X + padding, yOffset, labelColWidth, valueColX,
                            "Bit Depth", $"{info.BitDepth}-bit");
                        yOffset += iLineH;
                    }

                    // Channels
                    string channelText = info.Channels == 1 ? "Mono"
                                       : info.Channels == 2 ? "Stereo"
                                       : info.Channels > 0  ? $"{info.Channels} channels"
                                       : string.Empty;
                    if (!string.IsNullOrEmpty(channelText))
                    {
                        DrawGridRow(g, bodyFont, labelBrush, valueBrush,
                            bounds.X + padding, yOffset, labelColWidth, valueColX,
                            "Channels", channelText);
                        yOffset += iLineH;
                    }

                    // Duration
                    if (info.Duration > 0)
                    {
                        DrawGridRow(g, bodyFont, labelBrush, valueBrush,
                            bounds.X + padding, yOffset, labelColWidth, valueColX,
                            "Duration", FormatDuration(info.Duration));
                        yOffset += iLineH;
                    }

                    // Bitrate (skip if 0, or if module format — not meaningful for .mod/.xm/.it/.s3m)
                    if (info.BitRate > 0 && !info.IsModuleFormat)
                    {
                        DrawGridRow(g, bodyFont, labelBrush, valueBrush,
                            bounds.X + padding, yOffset, labelColWidth, valueColX,
                            "Bitrate", $"{info.BitRate} kbps");
                        yOffset += iLineH;
                    }
                }
            }

            return yOffset;
        }

        /// <summary>
        /// Draws the tag metadata grid (Title, Artist, Album).
        /// Only call this when at least one tag is present.
        /// Returns the updated y-offset after the grid.
        /// </summary>
        private static int DrawTagGrid(Graphics g, Rectangle bounds, AudioFileInfo info,
            int yOffset, int padding, float dpiScale)
        {
            Color textColor = ThemeHelper.GetTextColor();
            Color secondaryTextColor = ThemeHelper.GetSecondaryTextColor();

            int availableWidth = bounds.Width - padding * 2;
            int labelColWidth = (int)(availableWidth * 0.28f);
            int valueColX = bounds.X + padding + labelColWidth + (int)(4 * dpiScale);

            using (Font bodyFont = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point))
            using (Brush valueBrush = new SolidBrush(textColor))
            using (Brush labelBrush = new SolidBrush(secondaryTextColor))
            {
                float lineH = bodyFont.GetHeight(g) * 1.35f;
                int iLineH = (int)lineH;

                if (!string.IsNullOrEmpty(info.Title))
                {
                    DrawGridRow(g, bodyFont, labelBrush, valueBrush,
                        bounds.X + padding, yOffset, labelColWidth, valueColX,
                        "Title", info.Title!);
                    yOffset += iLineH;
                }

                if (!string.IsNullOrEmpty(info.Artist))
                {
                    DrawGridRow(g, bodyFont, labelBrush, valueBrush,
                        bounds.X + padding, yOffset, labelColWidth, valueColX,
                        "Artist", info.Artist!);
                    yOffset += iLineH;
                }

                if (!string.IsNullOrEmpty(info.Album))
                {
                    DrawGridRow(g, bodyFont, labelBrush, valueBrush,
                        bounds.X + padding, yOffset, labelColWidth, valueColX,
                        "Album", info.Album!);
                    yOffset += iLineH;
                }
            }

            return yOffset;
        }

        /// <summary>
        /// Draws the "Music Info" section with Key and BPM rows, including analysis state:
        /// detected/tag labels, confidence percentages, analysis progress, re-analyze button.
        /// </summary>
        private static void DrawMusicInfoSection(Graphics g, Rectangle bounds, AudioFileInfo info,
            int yOffset, int padding, float dpiScale,
            AnalysisResult? analysisResult, bool isAnalyzing, float analysisProgress,
            bool isReanalyzing, bool isReanalyzeHovered,
            ref Rectangle reanalyzeButtonBounds)
        {
            Color textColor = ThemeHelper.GetTextColor();
            Color secondaryTextColor = ThemeHelper.GetSecondaryTextColor();

            int availableWidth = bounds.Width - padding * 2;
            int labelColWidth = (int)(availableWidth * 0.28f);
            int valueColX = bounds.X + padding + labelColWidth + (int)(4 * dpiScale);

            // Determine if re-analyze button should be visible:
            // Show when there is an analysis result (detected or failed) AND at least one value came from detection
            bool hasDetectedValues = analysisResult != null;
            bool hasAtLeastOneDetected = hasDetectedValues
                && (info.Bpm == null || info.Key == null); // at least one value came from detection

            // Section header "Music Info" — same style as filename but smaller
            // Draw re-analyze button to the right of the header if applicable
            int buttonSize = (int)(18 * dpiScale);
            using (Font headerFont = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point))
            using (Brush secondaryBrush = new SolidBrush(secondaryTextColor))
            {
                float lineH = headerFont.GetHeight(g) * 1.35f;
                int iLineH = (int)lineH;

                g.DrawString("Music Info", headerFont, secondaryBrush,
                    bounds.X + padding, yOffset);

                // Draw re-analyze button (refresh icon) to right of header
                if (hasAtLeastOneDetected)
                {
                    int btnX = bounds.Right - padding - buttonSize;
                    int btnY = yOffset + (iLineH - buttonSize) / 2;
                    Rectangle btnRect = new Rectangle(btnX, btnY, buttonSize, buttonSize);
                    reanalyzeButtonBounds = btnRect;

                    DrawReanalyzeButton(g, btnRect, isReanalyzeHovered, dpiScale);

                    // Draw tooltip when hovered
                    if (isReanalyzeHovered)
                    {
                        DrawTooltip(g, btnRect, "Re-analyze BPM/Key", dpiScale);
                    }
                }
                else
                {
                    reanalyzeButtonBounds = Rectangle.Empty;
                }

                yOffset += iLineH;
            }

            using (Font bodyFont = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point))
            using (Brush valueBrush = new SolidBrush(textColor))
            using (Brush labelBrush = new SolidBrush(secondaryTextColor))
            {
                float lineH = bodyFont.GetHeight(g) * 1.35f;
                int iLineH = (int)lineH;
                int progressPct = (int)(analysisProgress * 100f);

                // Compute a fixed annotation column X so "(detected ...)" and "(tag)" align across rows.
                // Measure the widest expected value text to set the annotation start point.
                int annotationColX = valueColX + (int)g.MeasureString("000 BPM  ", bodyFont).Width;

                // Key row (before BPM per user decision)
                string keyValue = "-";
                string? keyAnnotation = null;
                Brush keyValueBrush = valueBrush;
                Brush? keyAnnotationBrush = null;

                if (isAnalyzing && !isReanalyzing)
                {
                    keyValue = $"Analyzing... {progressPct}%";
                    keyValueBrush = new SolidBrush(secondaryTextColor);
                }
                else if (isReanalyzing && analysisResult?.DetectedKey != null)
                {
                    keyValue = analysisResult.DetectedKey!;
                    keyAnnotation = $"(detected \u2014 {(int)(analysisResult.KeyConfidence * 100)}% confidence)";
                    keyValueBrush = new SolidBrush(Color.FromArgb(128, textColor));
                    keyAnnotationBrush = new SolidBrush(Color.FromArgb(128, secondaryTextColor));
                }
                else if (isReanalyzing && analysisResult?.KeyFailed == true)
                {
                    keyValue = "\u2014";
                    keyAnnotation = "(unable to detect)";
                    keyValueBrush = new SolidBrush(Color.FromArgb(128, secondaryTextColor));
                    keyAnnotationBrush = new SolidBrush(Color.FromArgb(128, secondaryTextColor));
                }
                else if (!string.IsNullOrEmpty(info.Key))
                {
                    keyValue = info.Key!;
                    keyAnnotation = "(tag)";
                }
                else if (analysisResult?.DetectedKey != null)
                {
                    keyValue = analysisResult.DetectedKey!;
                    keyAnnotation = $"(detected \u2014 {(int)(analysisResult.KeyConfidence * 100)}% confidence)";
                }
                else if (analysisResult?.KeyFailed == true)
                {
                    keyValue = "\u2014";
                    keyAnnotation = "(unable to detect)";
                    keyValueBrush = new SolidBrush(secondaryTextColor);
                    keyAnnotationBrush = new SolidBrush(secondaryTextColor);
                }
                else
                {
                    keyValue = "-";
                }

                DrawGridRow(g, bodyFont, labelBrush, keyValueBrush,
                    bounds.X + padding, yOffset, labelColWidth, valueColX,
                    "Key", keyValue);
                if (keyAnnotation != null)
                {
                    using (Brush aBrush = keyAnnotationBrush ?? new SolidBrush(secondaryTextColor))
                    {
                        DrawAnnotation(g, bodyFont, aBrush, annotationColX, yOffset, bounds, keyAnnotation);
                    }
                }
                yOffset += iLineH;

                if (keyValueBrush != valueBrush)
                    keyValueBrush.Dispose();

                // Show re-analysis progress below key row when reanalyzing
                if (isReanalyzing && isAnalyzing)
                {
                    using (Brush progressBrush = new SolidBrush(secondaryTextColor))
                    {
                        DrawGridRow(g, bodyFont, labelBrush, progressBrush,
                            bounds.X + padding, yOffset, labelColWidth, valueColX,
                            "", $"Analyzing... {progressPct}%");
                        yOffset += iLineH;
                    }
                }

                // BPM row (displayed as whole number; dash if absent)
                string bpmValue;
                string? bpmAnnotation = null;
                Brush bpmValueBrush = valueBrush;
                Brush? bpmAnnotationBrush = null;

                if (isAnalyzing && !isReanalyzing)
                {
                    bpmValue = $"Analyzing... {progressPct}%";
                    bpmValueBrush = new SolidBrush(secondaryTextColor);
                }
                else if (isReanalyzing && analysisResult?.DetectedBpm != null)
                {
                    bpmValue = $"{analysisResult.DetectedBpm} ";
                    bpmAnnotation = $"(detected \u2014 {(int)(analysisResult.BpmConfidence * 100)}% confidence)";
                    bpmValueBrush = new SolidBrush(Color.FromArgb(128, textColor));
                    bpmAnnotationBrush = new SolidBrush(Color.FromArgb(128, secondaryTextColor));
                }
                else if (isReanalyzing && analysisResult?.BpmFailed == true)
                {
                    bpmValue = "\u2014";
                    bpmAnnotation = "(unable to detect)";
                    bpmValueBrush = new SolidBrush(Color.FromArgb(128, secondaryTextColor));
                    bpmAnnotationBrush = new SolidBrush(Color.FromArgb(128, secondaryTextColor));
                }
                else if (info.Bpm.HasValue)
                {
                    bpmValue = $"{info.Bpm.Value} ";
                    bpmAnnotation = "(tag)";
                }
                else if (analysisResult?.DetectedBpm != null)
                {
                    bpmValue = $"{analysisResult.DetectedBpm} ";
                    bpmAnnotation = $"(detected \u2014 {(int)(analysisResult.BpmConfidence * 100)}% confidence)";
                }
                else if (analysisResult?.BpmFailed == true)
                {
                    bpmValue = "\u2014";
                    bpmAnnotation = "(unable to detect)";
                    bpmValueBrush = new SolidBrush(secondaryTextColor);
                    bpmAnnotationBrush = new SolidBrush(secondaryTextColor);
                }
                else
                {
                    bpmValue = "-";
                }

                DrawGridRow(g, bodyFont, labelBrush, bpmValueBrush,
                    bounds.X + padding, yOffset, labelColWidth, valueColX,
                    "BPM", bpmValue);
                if (bpmAnnotation != null)
                {
                    using (Brush aBrush = bpmAnnotationBrush ?? new SolidBrush(secondaryTextColor))
                    {
                        DrawAnnotation(g, bodyFont, aBrush, annotationColX, yOffset, bounds, bpmAnnotation);
                    }
                }

                if (bpmValueBrush != valueBrush)
                    bpmValueBrush.Dispose();
            }
        }

        /// <summary>
        /// Draws the re-analyze button: a small circular refresh icon using GDI+ arcs.
        /// </summary>
        private static void DrawReanalyzeButton(Graphics g, Rectangle rect, bool isHovered, float dpiScale)
        {
            Color bgColor = ThemeHelper.GetBackgroundColor();
            Color textColor = ThemeHelper.GetTextColor();

            // Button background (slightly highlighted when hovered)
            if (isHovered)
            {
                bool isDark = ThemeHelper.IsSystemInDarkMode();
                Color hoverBg = isDark
                    ? Color.FromArgb(60, 255, 255, 255)
                    : Color.FromArgb(40, 0, 0, 0);
                using (Brush hoverBrush = new SolidBrush(hoverBg))
                {
                    g.FillRectangle(hoverBrush, rect);
                }
            }

            // Draw circular refresh arrow arc
            int penWidth = Math.Max(1, (int)(1.5f * dpiScale));
            int inset = Math.Max(2, (int)(3 * dpiScale));
            Rectangle arcRect = new Rectangle(rect.X + inset, rect.Y + inset,
                rect.Width - inset * 2, rect.Height - inset * 2);

            using (Pen pen = new Pen(textColor, penWidth))
            {
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;

                // Main arc: 30 degrees start, 270 degrees sweep (3/4 circle)
                g.DrawArc(pen, arcRect, 30, 270);

                // Arrowhead at the end of the arc (pointing clockwise)
                // End point of arc at angle 30+270 = 300 degrees
                double endAngleRad = 300.0 * Math.PI / 180.0;
                float cx = arcRect.X + arcRect.Width / 2.0f;
                float cy = arcRect.Y + arcRect.Height / 2.0f;
                float rx = arcRect.Width / 2.0f;
                float ry = arcRect.Height / 2.0f;

                float endX = cx + (float)(rx * Math.Cos(endAngleRad));
                float endY = cy + (float)(ry * Math.Sin(endAngleRad));

                // Arrowhead: two short lines
                float arrowLen = (float)(3 * dpiScale);
                g.DrawLine(pen, endX, endY, endX - arrowLen, endY - arrowLen / 2);
                g.DrawLine(pen, endX, endY, endX + arrowLen / 2, endY - arrowLen);
            }
        }

        /// <summary>
        /// Draws a simple tooltip box near the button.
        /// </summary>
        private static void DrawTooltip(Graphics g, Rectangle buttonRect, string text, float dpiScale)
        {
            Color bgColor = ThemeHelper.GetBackgroundColor();
            Color textColor = ThemeHelper.GetTextColor();
            Color borderColor = ThemeHelper.GetSecondaryTextColor();

            using (Font tipFont = new Font("Segoe UI", 8.0f, FontStyle.Regular, GraphicsUnit.Point))
            {
                SizeF textSize = g.MeasureString(text, tipFont);
                int tipPad = (int)(3 * dpiScale);
                int tipW = (int)textSize.Width + tipPad * 2;
                int tipH = (int)textSize.Height + tipPad * 2;

                // Position tooltip above and to the left of the button
                int tipX = Math.Max(0, buttonRect.Right - tipW);
                int tipY = buttonRect.Top - tipH - (int)(2 * dpiScale);

                Rectangle tipRect = new Rectangle(tipX, tipY, tipW, tipH);

                using (Brush tipBg = new SolidBrush(bgColor))
                using (Pen borderPen = new Pen(borderColor))
                using (Brush tipText = new SolidBrush(textColor))
                {
                    g.FillRectangle(tipBg, tipRect);
                    g.DrawRectangle(borderPen, tipRect);
                    g.DrawString(text, tipFont, tipText, tipX + tipPad, tipY + tipPad);
                }
            }
        }

        /// <summary>
        /// Hit tests whether the given point is within the re-analyze button bounds.
        /// Returns false when no re-analyze button is visible (bounds are empty).
        /// </summary>
        public static bool HitTestReanalyze(Rectangle reanalyzeButtonBounds, Point point)
        {
            return !reanalyzeButtonBounds.IsEmpty && reanalyzeButtonBounds.Contains(point);
        }

        /// <summary>
        /// Draws a single label-value row in the metadata grid.
        /// </summary>
        private static void DrawGridRow(Graphics g, Font font, Brush labelBrush, Brush valueBrush,
            int labelX, int y, int labelWidth, int valueX, string label, string value)
        {
            using (StringFormat ellipsis = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                float rowHeight = font.GetHeight(g) * 1.5f;

                if (!string.IsNullOrEmpty(label))
                {
                    g.DrawString(label, font, labelBrush,
                        new RectangleF(labelX, y, labelWidth, rowHeight), ellipsis);
                }

                // Value gets remaining width to the right
                float valueWidth = g.ClipBounds.Right - valueX - 4;
                if (valueWidth > 10)
                {
                    g.DrawString(value, font, valueBrush,
                        new RectangleF(valueX, y, valueWidth, rowHeight), ellipsis);
                }
            }
        }

        /// <summary>
        /// Draws an annotation string (e.g. "(tag)", "(detected ...)") at a fixed X column for alignment.
        /// </summary>
        private static void DrawAnnotation(Graphics g, Font font, Brush brush,
            int annotationX, int y, Rectangle bounds, string text)
        {
            float rowHeight = font.GetHeight(g) * 1.5f;
            float availableWidth = bounds.Right - annotationX - 4;
            if (availableWidth > 10)
            {
                using (StringFormat sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                {
                    g.DrawString(text, font, brush,
                        new RectangleF(annotationX, y, availableWidth, rowHeight), sf);
                }
            }
        }

        /// <summary>
        /// Formats a file size in bytes to human-readable format.
        /// </summary>
        public static string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            else if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F2} KB";
            else if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F2} MB";
            else
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        /// <summary>
        /// Formats a duration in seconds to mm:ss or hh:mm:ss format.
        /// </summary>
        public static string FormatDuration(double seconds)
        {
            TimeSpan ts = TimeSpan.FromSeconds(seconds);

            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            else
                return $"{ts.Minutes}:{ts.Seconds:D2}";
        }
    }
}
