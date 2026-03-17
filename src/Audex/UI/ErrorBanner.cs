using System;
using System.Drawing;
using Audex.Utils;

namespace Audex.UI
{
    /// <summary>
    /// Renders an error banner overlay at the top of the preview area.
    /// Shows user-friendly error message with log file path.
    /// </summary>
    public static class ErrorBanner
    {
        private const int BaseBannerHeight = 50;

        /// <summary>
        /// Draws the error banner at the top of the preview area.
        /// </summary>
        /// <param name="g">Graphics context for drawing</param>
        /// <param name="bounds">Full preview area bounds</param>
        /// <param name="errorMessage">Error message to display</param>
        /// <param name="logFilePath">Path to the log file</param>
        public static void Draw(Graphics g, Rectangle bounds, string errorMessage, string logFilePath)
        {
            if (g == null) return;

            // Calculate DPI scaling
            float dpiScale = g.DpiX / 96.0f;
            int bannerHeight = GetBannerHeight(dpiScale);

            // Draw banner background
            Rectangle bannerRect = new Rectangle(bounds.X, bounds.Y, bounds.Width, bannerHeight);
            using (Brush bgBrush = new SolidBrush(ThemeHelper.GetErrorBannerBackgroundColor()))
            {
                g.FillRectangle(bgBrush, bannerRect);
            }

            // Draw error text
            using (Brush textBrush = new SolidBrush(ThemeHelper.GetErrorBannerTextColor()))
            using (Font font = new Font("Segoe UI", 9.0f * dpiScale))
            {
                string displayText = $"This file can't be previewed. See log for details: {logFilePath}";

                // Add padding
                int padding = (int)(8 * dpiScale);
                Rectangle textRect = new Rectangle(
                    bannerRect.X + padding,
                    bannerRect.Y + padding,
                    bannerRect.Width - (padding * 2),
                    bannerRect.Height - (padding * 2)
                );

                // Draw with word wrap
                using (StringFormat format = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Near,
                    Trimming = StringTrimming.EllipsisCharacter
                })
                {
                    g.DrawString(displayText, font, textBrush, textRect, format);
                }
            }
        }

        /// <summary>
        /// Returns the height of the error banner in pixels, scaled by DPI.
        /// </summary>
        public static int GetBannerHeight(float dpiScale)
        {
            return (int)(BaseBannerHeight * dpiScale);
        }
    }
}
