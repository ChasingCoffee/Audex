using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TagLib;
using Audex.Utils;

namespace Audex.Audio
{
    /// <summary>
    /// Reads ID3/Vorbis/APE tag metadata from audio files via TagLib# IFileAbstraction.
    /// Accepts raw byte arrays so no temporary files are needed.
    /// </summary>
    public static class TagReader
    {
        /// <summary>
        /// Extracts tag metadata (Title, Artist, Album) from the supplied audio byte array.
        /// The fileName MUST include the file extension for TagLib# format detection.
        /// Returns a TagInfo with all-null fields if the format is unsupported or tags are absent.
        /// </summary>
        public static TagInfo ReadTags(byte[] data, string fileName)
        {
            try
            {
                using var abstraction = new ByteArrayFileAbstraction(fileName, data);
                using TagLib.File tagFile = TagLib.File.Create(abstraction);

                Tag tag = tagFile.Tag;

                string? title = string.IsNullOrWhiteSpace(tag.Title) ? null : tag.Title.Trim();

                string? artist = null;
                if (tag.Performers != null && tag.Performers.Length > 0)
                {
                    string joined = string.Join(", ", tag.Performers);
                    artist = string.IsNullOrWhiteSpace(joined) ? null : joined.Trim();
                }

                string? album = string.IsNullOrWhiteSpace(tag.Album) ? null : tag.Album.Trim();

                return new TagInfo(title, artist, album);
            }
            catch (UnsupportedFormatException)
            {
                // Format not recognized by TagLib# — return nulls, not an error
                return new TagInfo(null, null, null);
            }
            catch (Exception ex)
            {
                Logger.Error($"[TagReader] Failed to read tags from '{fileName}': {ex.Message}", ex);
                return new TagInfo(null, null, null);
            }
        }

        /// <summary>
        /// Reads BPM and musical key from all available tag types.
        ///
        /// BPM sources (first non-null wins by default; "most precise" wins across all):
        ///   1. ID3v2 TBPM frame — covers Traktor (writes standard TBPM) and Serato (also writes TBPM)
        ///   2. Vorbis Comment "BPM"
        ///   3. APE "BPM"
        ///   4. Serato Autotags GEOB frame — fallback for files analyzed by Serato without standard TBPM
        ///
        /// Key sources (first non-null wins):
        ///   1. ID3v2 TKEY frame — covers Traktor (writes standard TKEY) and rekordbox (writes TKEY when enabled)
        ///   2. Vorbis Comment "INITIALKEY"
        ///   3. APE "INITIALKEY"
        ///
        /// DJ software coverage:
        ///   - Traktor: Writes standard TBPM + TKEY — fully covered by sources 1 above.
        ///   - rekordbox: Writes TKEY to ID3 (covered by key source 1). Does NOT write TBPM —
        ///     known rekordbox limitation; BPM detection fills this gap when enabled.
        ///   - Serato: Writes standard TBPM/TKEY AND Serato Autotags GEOB.
        ///     Standard tags covered by source 1; GEOB is fallback when standard TBPM absent.
        ///
        /// Key normalization: MusicKeyNormalizer.Normalize() converts Camelot (8A/8B),
        /// Open Key (1d/1m), and text forms (A minor) to standard notation (Am, C#m, F).
        /// </summary>
        public static MusicInfo ReadMusicInfo(byte[] data, string fileName)
        {
            try
            {
                using var abstraction = new ByteArrayFileAbstraction(fileName, data);
                using TagLib.File tagFile = TagLib.File.Create(abstraction);

                // Get all available tag types
                TagLib.Id3v2.Tag? id3v2 = tagFile.GetTag(TagTypes.Id3v2) as TagLib.Id3v2.Tag;
                TagLib.Ogg.XiphComment? xiph = tagFile.GetTag(TagTypes.Xiph) as TagLib.Ogg.XiphComment;
                TagLib.Ape.Tag? ape = tagFile.GetTag(TagTypes.Ape) as TagLib.Ape.Tag;

                // ---- Collect BPM values from all sources (most precise wins) ----
                var bpmCandidates = new List<string>();

                // Source 1: ID3v2 TBPM (Traktor + Serato standard tag)
                string? tbpm = GetId3v2TextFrame(id3v2, "TBPM");
                if (!string.IsNullOrWhiteSpace(tbpm)) bpmCandidates.Add(tbpm!);

                // Source 2: Vorbis Comment BPM
                string? xiphBpm = GetXiphField(xiph, "BPM");
                if (!string.IsNullOrWhiteSpace(xiphBpm)) bpmCandidates.Add(xiphBpm!);

                // Source 3: APE BPM
                string? apeBpm = GetApeField(ape, "BPM");
                if (!string.IsNullOrWhiteSpace(apeBpm)) bpmCandidates.Add(apeBpm!);

                // Source 4: Serato Autotags GEOB (fallback when standard TBPM absent)
                if (bpmCandidates.Count == 0)
                {
                    string? seratoBpm = ReadSeratoAutotagsBpm(id3v2);
                    if (!string.IsNullOrWhiteSpace(seratoBpm)) bpmCandidates.Add(seratoBpm!);
                }

                // Select BPM winner: most decimal places (most precise) wins
                int? bpm = SelectMostPreciseBpm(bpmCandidates);

                // ---- Read Key from first available source ----
                string? rawKey = null;

                // Source 1: ID3v2 TKEY (Traktor + rekordbox standard tag)
                rawKey = GetId3v2TextFrame(id3v2, "TKEY");

                // Source 2: Vorbis Comment INITIALKEY
                if (string.IsNullOrWhiteSpace(rawKey))
                    rawKey = GetXiphField(xiph, "INITIALKEY");

                // Source 3: APE INITIALKEY
                if (string.IsNullOrWhiteSpace(rawKey))
                    rawKey = GetApeField(ape, "INITIALKEY");

                // Normalize key to standard notation
                string? normalizedKey = MusicKeyNormalizer.Normalize(rawKey);

                return new MusicInfo(bpm, normalizedKey);
            }
            catch (UnsupportedFormatException)
            {
                // Format not recognized by TagLib# — no music info available
                return new MusicInfo(null, null);
            }
            catch (Exception ex)
            {
                Logger.Error($"[TagReader] ReadMusicInfo failed for '{fileName}': {ex.Message}", ex);
                return new MusicInfo(null, null);
            }
        }

        // -------------------------------------------------------------------------
        // Tag-reading helpers
        // -------------------------------------------------------------------------

        /// <summary>
        /// Reads the text value of an ID3v2 frame by frame ID (e.g., "TBPM", "TKEY").
        /// Returns null if the tag is null, the frame is absent, or the value is empty.
        /// </summary>
        private static string? GetId3v2TextFrame(TagLib.Id3v2.Tag? tag, string frameId)
        {
            if (tag == null) return null;

            foreach (TagLib.Id3v2.Frame frame in tag.GetFrames(frameId))
            {
                if (frame is TagLib.Id3v2.TextInformationFrame textFrame)
                {
                    string? val = textFrame.ToString();
                    if (!string.IsNullOrWhiteSpace(val))
                        return val.Trim();
                }
            }

            return null;
        }

        /// <summary>
        /// Reads a field from Vorbis Comments (Ogg/FLAC/Opus tags) by field name.
        /// Field names are case-insensitive per the Vorbis Comment spec.
        /// Returns null if the tag is null, the field is absent, or the value is empty.
        /// </summary>
        private static string? GetXiphField(TagLib.Ogg.XiphComment? tag, string fieldName)
        {
            if (tag == null) return null;

            string[]? values = tag.GetField(fieldName);
            if (values != null && values.Length > 0)
            {
                string val = values[0];
                if (!string.IsNullOrWhiteSpace(val))
                    return val.Trim();
            }

            return null;
        }

        /// <summary>
        /// Reads a field from an APE tag by field name.
        /// Returns null if the tag is null, the item is absent, or the value is empty.
        /// </summary>
        private static string? GetApeField(TagLib.Ape.Tag? tag, string fieldName)
        {
            if (tag == null) return null;

            TagLib.Ape.Item? item = tag.GetItem(fieldName);
            if (item != null)
            {
                string val = item.ToString();
                if (!string.IsNullOrWhiteSpace(val))
                    return val.Trim();
            }

            return null;
        }

        /// <summary>
        /// Reads BPM from a Serato Autotags GEOB frame.
        /// Serato encodes analysis results (BPM, auto-gain) in a GEOB frame with
        /// Description = "Serato Autotags". The binary layout is:
        ///   - 2-byte version header
        ///   - null-terminated ASCII BPM string
        ///   - null-terminated ASCII auto-gain string (ignored)
        /// Returns null on any parsing error (format may vary; always defensive).
        ///
        /// Note: TagLib# GeneralEncapsulatedObjectFrame is marked obsolete in favor of
        /// AttachmentFrame, but GEOB frames are still parsed correctly; we suppress
        /// the obsolete warning and work with ByteVector.Data.
        /// </summary>
#pragma warning disable CS0618 // GeneralEncapsulatedObjectFrame is obsolete but functional for GEOB
        private static string? ReadSeratoAutotagsBpm(TagLib.Id3v2.Tag? tag)
        {
            if (tag == null) return null;

            try
            {
                foreach (TagLib.Id3v2.Frame frame in tag.GetFrames("GEOB"))
                {
                    if (frame is TagLib.Id3v2.GeneralEncapsulatedObjectFrame geob)
                    {
                        if (!string.Equals(geob.Description, "Serato Autotags",
                            StringComparison.OrdinalIgnoreCase))
                            continue;

                        // TagLib# returns ByteVector; convert to byte[] for processing
                        TagLib.ByteVector bv = geob.Object;
                        if (bv == null || bv.Count < 4)
                            return null;

                        byte[] frameData = bv.Data;

                        // Skip 2-byte header, then read null-terminated ASCII BPM string
                        int offset = 2;
                        int nullPos = -1;
                        for (int i = offset; i < frameData.Length; i++)
                        {
                            if (frameData[i] == 0) { nullPos = i; break; }
                        }
                        if (nullPos < 0) nullPos = frameData.Length;

                        int length = nullPos - offset;
                        if (length <= 0) return null;

                        string bpmStr = Encoding.ASCII.GetString(frameData, offset, length).Trim();
                        return string.IsNullOrWhiteSpace(bpmStr) ? null : bpmStr;
                    }
                }
            }
            catch
            {
                // Serato GEOB format may vary — silently return null
            }

            return null;
        }
#pragma warning restore CS0618

        /// <summary>
        /// Parses a BPM string as double, rounds to nearest integer, clamps to 1-999.
        /// Returns null if the string is unparseable or out of range.
        /// </summary>
        private static int? NormalizeBpm(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            if (!double.TryParse(raw!.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double val))
            {
                return null;
            }

            int rounded = (int)Math.Round(val, MidpointRounding.AwayFromZero);
            if (rounded < 1 || rounded > 999) return null;

            return rounded;
        }

        /// <summary>
        /// Selects the BPM candidate with the most decimal places (most precise).
        /// If equal precision, the first candidate wins.
        /// Returns null if no valid BPM string is found.
        /// </summary>
        private static int? SelectMostPreciseBpm(List<string> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;

            string? bestRaw = null;
            int bestDecimals = -1;

            foreach (string raw in candidates)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                // Count decimal places
                int dotIdx = raw.IndexOf('.');
                int decimals = dotIdx >= 0 ? raw.Length - dotIdx - 1 : 0;

                if (decimals > bestDecimals)
                {
                    bestDecimals = decimals;
                    bestRaw = raw;
                }
            }

            return NormalizeBpm(bestRaw);
        }

        // -------------------------------------------------------------------------
        // Inner class: IFileAbstraction that wraps a byte array as a readable stream
        // -------------------------------------------------------------------------

        private sealed class ByteArrayFileAbstraction : TagLib.File.IFileAbstraction, IDisposable
        {
            private readonly byte[] _data;
            private MemoryStream? _readStream;

            /// <summary>
            /// Filename including extension — TagLib# uses the extension for format detection.
            /// </summary>
            public string Name { get; }

            public Stream ReadStream
            {
                get
                {
                    // Lazy-create; reuse on subsequent reads
                    if (_readStream == null || !_readStream.CanRead)
                    {
                        _readStream?.Dispose();
                        _readStream = new MemoryStream(_data, writable: false);
                    }
                    return _readStream;
                }
            }

            /// <summary>TagLib# will never write to this abstraction.</summary>
            public Stream WriteStream => throw new NotSupportedException(
                "ByteArrayFileAbstraction is read-only.");

            public ByteArrayFileAbstraction(string fileName, byte[] data)
            {
                Name = fileName ?? throw new ArgumentNullException(nameof(fileName));
                _data = data ?? throw new ArgumentNullException(nameof(data));
            }

            public void CloseStream(Stream stream)
            {
                stream?.Dispose();
            }

            public void Dispose()
            {
                _readStream?.Dispose();
                _readStream = null;
            }
        }
    }

    /// <summary>
    /// Simple value type holding tag metadata extracted by TagReader.
    /// Null fields indicate absent or empty tags.
    /// </summary>
    public sealed class TagInfo
    {
        /// <summary>Track title, or null if absent/empty.</summary>
        public string? Title { get; }

        /// <summary>Artist name (Performers joined with ", "), or null if absent/empty.</summary>
        public string? Artist { get; }

        /// <summary>Album name, or null if absent/empty.</summary>
        public string? Album { get; }

        public TagInfo(string? title, string? artist, string? album)
        {
            Title = title;
            Artist = artist;
            Album = album;
        }
    }

    /// <summary>
    /// Holds BPM and musical key metadata read by TagReader.ReadMusicInfo().
    /// Null fields indicate the value was not present in any available tag source.
    /// </summary>
    public sealed class MusicInfo
    {
        /// <summary>BPM (beats per minute), rounded to nearest integer. Null if not available.</summary>
        public int? Bpm { get; }

        /// <summary>Musical key in standard notation (e.g., Am, C#m, F). Null if not available.</summary>
        public string? Key { get; }

        public MusicInfo(int? bpm, string? key)
        {
            Bpm = bpm;
            Key = key;
        }
    }
}
