using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Audex.Audio
{
    /// <summary>
    /// Normalizes musical key notation to standard form (e.g., Am, C#m, F).
    /// Handles Camelot Wheel notation (8A, 8B), Open Key notation (1d, 1m),
    /// and common text variants (A minor, C# major).
    /// </summary>
    public static class MusicKeyNormalizer
    {
        // Camelot Wheel -> standard key notation
        // 1A-12A = minor keys, 1B-12B = major keys
        private static readonly Dictionary<string, string> CamelotMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "1A",  "Abm" }, { "1B",  "B"   },
            { "2A",  "Ebm" }, { "2B",  "F#"  },
            { "3A",  "Bbm" }, { "3B",  "Db"  },
            { "4A",  "Fm"  }, { "4B",  "Ab"  },
            { "5A",  "Cm"  }, { "5B",  "Eb"  },
            { "6A",  "Gm"  }, { "6B",  "Bb"  },
            { "7A",  "Dm"  }, { "7B",  "F"   },
            { "8A",  "Am"  }, { "8B",  "C"   },
            { "9A",  "Em"  }, { "9B",  "G"   },
            { "10A", "Bm"  }, { "10B", "D"   },
            { "11A", "F#m" }, { "11B", "A"   },
            { "12A", "C#m" }, { "12B", "E"   },
        };

        // Open Key notation -> standard key notation
        // Nd = major (dominant), Nm = minor
        private static readonly Dictionary<string, string> OpenKeyMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "1d",  "C"   }, { "1m",  "Am"  },
            { "2d",  "G"   }, { "2m",  "Em"  },
            { "3d",  "D"   }, { "3m",  "Bm"  },
            { "4d",  "A"   }, { "4m",  "F#m" },
            { "5d",  "E"   }, { "5m",  "C#m" },
            { "6d",  "B"   }, { "6m",  "Abm" },
            { "7d",  "F#"  }, { "7m",  "Ebm" },
            { "8d",  "Db"  }, { "8m",  "Bbm" },
            { "9d",  "Ab"  }, { "9m",  "Fm"  },
            { "10d", "Eb"  }, { "10m", "Cm"  },
            { "11d", "Bb"  }, { "11m", "Gm"  },
            { "12d", "F"   }, { "12m", "Dm"  },
        };

        // All 24 known standard key names for quick lookup
        private static readonly HashSet<string> _standardKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            // Major keys
            "C", "Db", "D", "Eb", "E", "F", "F#", "Gb", "G", "Ab", "A", "Bb", "B",
            // Minor keys
            "Cm", "C#m", "Dbm", "Dm", "Ebm", "Em", "Fm", "F#m", "Gbm",
            "Gm", "Abm", "Am", "Bbm", "Bm",
        };

        // Regex: matches "A minor", "C# min", "Bb m", "F#minor", etc.
        private static readonly Regex _minorTextRegex = new Regex(
            @"^([A-Ga-g][#b]?)\s*(minor|min|m)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Regex: matches "C major", "Db maj", "G", etc. — root only or with major keyword
        private static readonly Regex _majorTextRegex = new Regex(
            @"^([A-Ga-g][#b]?)\s*(major|maj)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Normalizes a raw key string to standard musical notation.
        /// Returns the normalized key (e.g., "Am", "C#m", "F#"), or null if input is null/empty.
        /// If the format is unrecognized, returns the raw input trimmed (better than showing nothing).
        /// </summary>
        public static string? Normalize(string? raw)
        {
            if (raw == null) return null;
            string trimmed = raw.Trim();
            if (trimmed.Length == 0) return null;

            // 1. Check Camelot Wheel (case-insensitive on upper)
            if (CamelotMap.TryGetValue(trimmed.ToUpperInvariant(), out string? camelotResult))
                return camelotResult;

            // 2. Check Open Key (case-insensitive on lower)
            if (OpenKeyMap.TryGetValue(trimmed.ToLowerInvariant(), out string? openKeyResult))
                return openKeyResult;

            // 3. Check if already standard notation
            if (IsStandardNotation(trimmed))
                return NormalizeCase(trimmed);

            // 4. Check text variants (minor)
            var minorMatch = _minorTextRegex.Match(trimmed);
            if (minorMatch.Success)
            {
                string root = NormalizeRoot(minorMatch.Groups[1].Value);
                return root + "m";
            }

            // 5. Check text variants (major or bare root)
            var majorMatch = _majorTextRegex.Match(trimmed);
            if (majorMatch.Success)
            {
                string root = NormalizeRoot(majorMatch.Groups[1].Value);
                return root;
            }

            // 6. Unrecognized — return as-is (better to show something than nothing)
            return trimmed;
        }

        /// <summary>
        /// Returns true if the string is a valid standard key notation.
        /// Valid: root (A-G) + optional sharp/flat (# or b) + optional "m" for minor.
        /// </summary>
        private static bool IsStandardNotation(string s)
        {
            return _standardKeys.Contains(s)
                || _standardKeys.Contains(NormalizeCase(s));
        }

        /// <summary>
        /// Normalizes the root note to title case (e.g., "c#" -> "C#", "bb" -> "Bb").
        /// </summary>
        private static string NormalizeRoot(string root)
        {
            if (root.Length == 0) return root;
            return char.ToUpperInvariant(root[0]) + root.Substring(1).ToLowerInvariant();
        }

        /// <summary>
        /// Normalizes full key string case: root note uppercase, accidental lowercase, 'm' lowercase.
        /// E.g., "AM" -> "Am", "c#M" -> "C#m", "F#M" -> "F#m".
        /// </summary>
        private static string NormalizeCase(string s)
        {
            if (s.Length == 0) return s;

            // Determine root end (may include #/b)
            int rootEnd = 1;
            if (s.Length > 1 && (s[1] == '#' || s[1] == 'b'))
                rootEnd = 2;

            string root = char.ToUpperInvariant(s[0])
                        + (rootEnd > 1 ? s[1].ToString().ToLowerInvariant() : "");

            // Remainder is optional "m" / "M" for minor
            string suffix = s.Substring(rootEnd).ToLowerInvariant();

            return root + suffix;
        }
    }
}
