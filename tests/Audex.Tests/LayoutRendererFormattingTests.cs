using System.Drawing;
using Audex.UI;
using FluentAssertions;
using Xunit;

namespace Audex.Tests
{
    public class LayoutRendererFormattingTests
    {
        [Theory]
        [InlineData(0, "0 B")]
        [InlineData(1023, "1023 B")]
        [InlineData(1024, "1.00 KB")]
        [InlineData(1536, "1.50 KB")]
        [InlineData(1048576, "1.00 MB")]
        [InlineData(1073741824, "1.00 GB")]
        public void FormatFileSize_UsesExpectedUnits(long bytes, string expected)
        {
            LayoutRenderer.FormatFileSize(bytes).Should().Be(expected);
        }

        [Theory]
        [InlineData(0, "0:00")]
        [InlineData(59, "0:59")]
        [InlineData(60, "1:00")]
        [InlineData(3599, "59:59")]
        [InlineData(3600, "1:00:00")]
        [InlineData(3661, "1:01:01")]
        public void FormatDuration_UsesExpectedTimeFormat(double seconds, string expected)
        {
            LayoutRenderer.FormatDuration(seconds).Should().Be(expected);
        }

        [Fact]
        public void HitTestReanalyze_ReturnsFalseForEmptyBounds()
        {
            LayoutRenderer.HitTestReanalyze(Rectangle.Empty, new Point(10, 10))
                .Should().BeFalse();
        }

        [Fact]
        public void HitTestReanalyze_ReturnsTrueOnlyInsideBounds()
        {
            var bounds = new Rectangle(100, 200, 18, 18);

            LayoutRenderer.HitTestReanalyze(bounds, new Point(105, 205)).Should().BeTrue();
            LayoutRenderer.HitTestReanalyze(bounds, new Point(50, 50)).Should().BeFalse();
        }
    }
}
