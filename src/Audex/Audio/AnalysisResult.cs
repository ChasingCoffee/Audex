namespace Audex.Audio
{
    /// <summary>
    /// Result of BPM/key analysis. Returned by BpmKeyAnalyzer.Analyze() and persisted by AnalysisCache.
    /// </summary>
    public sealed class AnalysisResult
    {
        /// <summary>BPM rounded to nearest integer. Null if detection failed or was not attempted.</summary>
        public int? DetectedBpm { get; set; }

        /// <summary>Musical key in standard notation (Am, C, F#m, Bb, etc.). Null if detection failed.</summary>
        public string? DetectedKey { get; set; }

        /// <summary>BPM confidence 0.0-1.0. Heuristic: 0.92 for common range (60-200), 0.70 for extremes.</summary>
        public float BpmConfidence { get; set; }

        /// <summary>Key confidence 0.0-1.0. Derived from Pearson correlation magnitude.</summary>
        public float KeyConfidence { get; set; }

        /// <summary>Non-null reason string when both BPM and key detection failed entirely.</summary>
        public string? FailureReason { get; set; }

        /// <summary>True if BPM detection was attempted but failed (BASS returned -1 or error).</summary>
        public bool BpmFailed { get; set; }

        /// <summary>True if key detection was attempted but failed (silent audio or decode error).</summary>
        public bool KeyFailed { get; set; }
    }
}
