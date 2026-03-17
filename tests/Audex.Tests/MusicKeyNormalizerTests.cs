using Audex.Audio;
using FluentAssertions;
using Xunit;

namespace Audex.Tests
{
    public class MusicKeyNormalizerTests
    {
        // ── Null / empty / whitespace ──

        [Fact]
        public void Normalize_Null_ReturnsNull()
            => MusicKeyNormalizer.Normalize(null).Should().BeNull();

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Normalize_EmptyOrWhitespace_ReturnsNull(string input)
            => MusicKeyNormalizer.Normalize(input).Should().BeNull();

        // ── Camelot Wheel (all 24 keys) ──

        [Theory]
        [InlineData("1A",  "Abm")]
        [InlineData("2A",  "Ebm")]
        [InlineData("3A",  "Bbm")]
        [InlineData("4A",  "Fm")]
        [InlineData("5A",  "Cm")]
        [InlineData("6A",  "Gm")]
        [InlineData("7A",  "Dm")]
        [InlineData("8A",  "Am")]
        [InlineData("9A",  "Em")]
        [InlineData("10A", "Bm")]
        [InlineData("11A", "F#m")]
        [InlineData("12A", "C#m")]
        [InlineData("1B",  "B")]
        [InlineData("2B",  "F#")]
        [InlineData("3B",  "Db")]
        [InlineData("4B",  "Ab")]
        [InlineData("5B",  "Eb")]
        [InlineData("6B",  "Bb")]
        [InlineData("7B",  "F")]
        [InlineData("8B",  "C")]
        [InlineData("9B",  "G")]
        [InlineData("10B", "D")]
        [InlineData("11B", "A")]
        [InlineData("12B", "E")]
        public void Normalize_CamelotKey_ReturnsStandardNotation(string camelot, string expected)
            => MusicKeyNormalizer.Normalize(camelot).Should().Be(expected);

        [Theory]
        [InlineData("8a",  "Am")]
        [InlineData("8b",  "C")]
        [InlineData("11a", "F#m")]
        public void Normalize_CamelotKey_CaseInsensitive(string camelot, string expected)
            => MusicKeyNormalizer.Normalize(camelot).Should().Be(expected);

        // ── Open Key (all 24 keys) ──

        [Theory]
        [InlineData("1d",  "C")]
        [InlineData("2d",  "G")]
        [InlineData("3d",  "D")]
        [InlineData("4d",  "A")]
        [InlineData("5d",  "E")]
        [InlineData("6d",  "B")]
        [InlineData("7d",  "F#")]
        [InlineData("8d",  "Db")]
        [InlineData("9d",  "Ab")]
        [InlineData("10d", "Eb")]
        [InlineData("11d", "Bb")]
        [InlineData("12d", "F")]
        [InlineData("1m",  "Am")]
        [InlineData("2m",  "Em")]
        [InlineData("3m",  "Bm")]
        [InlineData("4m",  "F#m")]
        [InlineData("5m",  "C#m")]
        [InlineData("6m",  "Abm")]
        [InlineData("7m",  "Ebm")]
        [InlineData("8m",  "Bbm")]
        [InlineData("9m",  "Fm")]
        [InlineData("10m", "Cm")]
        [InlineData("11m", "Gm")]
        [InlineData("12m", "Dm")]
        public void Normalize_OpenKey_ReturnsStandardNotation(string openKey, string expected)
            => MusicKeyNormalizer.Normalize(openKey).Should().Be(expected);

        [Theory]
        [InlineData("1D",  "C")]
        [InlineData("1M",  "Am")]
        [InlineData("8D",  "Db")]
        public void Normalize_OpenKey_CaseInsensitive(string openKey, string expected)
            => MusicKeyNormalizer.Normalize(openKey).Should().Be(expected);

        // ── Already standard notation ──

        [Theory]
        [InlineData("Am",  "Am")]
        [InlineData("C",   "C")]
        [InlineData("F#",  "F#")]
        [InlineData("F#m", "F#m")]
        [InlineData("Bb",  "Bb")]
        [InlineData("Bbm", "Bbm")]
        [InlineData("Db",  "Db")]
        [InlineData("Ebm", "Ebm")]
        public void Normalize_StandardNotation_ReturnsSame(string key, string expected)
            => MusicKeyNormalizer.Normalize(key).Should().Be(expected);

        [Theory]
        [InlineData("am",  "Am")]
        [InlineData("AM",  "Am")]
        [InlineData("c#m", "C#m")]
        [InlineData("C#M", "C#m")]
        [InlineData("f#",  "F#")]
        public void Normalize_StandardNotation_NormalizesCase(string key, string expected)
            => MusicKeyNormalizer.Normalize(key).Should().Be(expected);

        // ── Text variants (minor) ──

        [Theory]
        [InlineData("A minor",  "Am")]
        [InlineData("A min",    "Am")]
        [InlineData("C# minor", "C#m")]
        [InlineData("Bb min",   "Bbm")]
        [InlineData("f minor",  "Fm")]
        [InlineData("g min",    "Gm")]
        public void Normalize_MinorTextVariant_ReturnsStandardMinor(string text, string expected)
            => MusicKeyNormalizer.Normalize(text).Should().Be(expected);

        // ── Text variants (major) ──

        [Theory]
        [InlineData("C major", "C")]
        [InlineData("C maj",   "C")]
        [InlineData("Db major", "Db")]
        [InlineData("f# maj",  "F#")]
        [InlineData("g major", "G")]
        public void Normalize_MajorTextVariant_ReturnsStandardMajor(string text, string expected)
            => MusicKeyNormalizer.Normalize(text).Should().Be(expected);

        // ── Bare root note ──

        [Theory]
        [InlineData("A", "A")]
        [InlineData("a", "A")]
        [InlineData("g", "G")]
        public void Normalize_BareRootNote_ReturnsUpperCase(string root, string expected)
            => MusicKeyNormalizer.Normalize(root).Should().Be(expected);

        // ── Unrecognized input returned as-is ──

        [Theory]
        [InlineData("Xm")]
        [InlineData("13A")]
        [InlineData("random text")]
        public void Normalize_Unrecognized_ReturnsTrimmedInput(string input)
            => MusicKeyNormalizer.Normalize(input).Should().Be(input);

        // ── Whitespace trimming ──

        [Fact]
        public void Normalize_LeadingTrailingWhitespace_Trimmed()
            => MusicKeyNormalizer.Normalize("  8A  ").Should().Be("Am");
    }
}
