namespace Audex.Audio
{
    /// <summary>Represents the playback lifecycle states of <see cref="AudioPlayer"/>.</summary>
    public enum AudioPlayerState
    {
        /// <summary>No file loaded.</summary>
        Idle,
        /// <summary>File is being loaded and decoded.</summary>
        Loading,
        /// <summary>Actively playing audio.</summary>
        Playing,
        /// <summary>Paused mid-track.</summary>
        Paused,
        /// <summary>Stopped with position reset to the beginning.</summary>
        Stopped,
        /// <summary>BASS initialization or stream error.</summary>
        Error
    }
}
