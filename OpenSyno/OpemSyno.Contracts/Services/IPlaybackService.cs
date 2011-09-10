﻿using System.Collections.Generic;

namespace OpenSyno.Services
{
    using System;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;

    /// <summary>
    /// The service in charge of managing the play queue, define which tracks comes next, handle random playback, repeat and other playback options.
    /// </summary>
    public interface IPlaybackService
    {
        /// <summary>
        /// Gets or sets what strategy should be used to define the next track to play.
        /// </summary>
        /// <value>The playback continuity.</value>
        PlaybackContinuity PlaybackContinuity { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the next track to be played should be preloaded.
        /// </summary>
        /// <value><c>true</c> if the next track to be played should be preloaded; otherwise, <c>false</c>.</value>
        bool PreloadTracks { get; set; }

        ///// <summary>
        ///// Gets the items in the playqueue.
        ///// </summary>
        ///// <value>The items in the playqueue.</value>
        //ObservableCollection<ISynoTrack> PlayqueueItems { get; }

        PlaybackStatus Status { get; }

        ///// <summary>
        ///// Clears the play queue.
        ///// </summary>
        //void ClearPlayQueue();

        ///// <summary>
        ///// Inserts the specified tracks to the play queue.
        ///// </summary>
        ///// <param name="tracks">The tracks.</param>
        ///// <param name="insertPosition"></param>
        //void InsertTracksToQueue(IEnumerable<ISynoTrack> tracks, int insertPosition);

        /// <summary>
        /// Plays the specified track. It must be present in the queue.
        /// </summary>
        /// <param name="trackToPlay">The track to play.</param>
        void PlayTrackInQueue(ISynoTrack trackToPlay);

        event TrackEndedDelegate TrackEnded;

        event TrackStartedDelegate TrackStarted;
        /// <summary>
        /// Occurs when the position of the playing track changes.
        /// </summary>
        event TrackCurrentPositionChangedDelegate TrackCurrentPositionChanged;

        event EventHandler<BufferingProgressUpdatedEventArgs> BufferingProgressUpdated;

        //ISynoTrack GetNextTrack(ISynoTrack currentTrack);

        void PausePlayback();

        void ResumePlayback();

        double GetVolume();

        void SetVolume(double volume);

        void SkipNext();

        event NotifyCollectionChangedEventHandler PlayqueueChanged;

        void InsertTracksToQueue(IEnumerable<ISynoTrack> tracks, int insertPosition);

        IEnumerable<ISynoTrack> GetTracksInQueue();
    }

    public delegate void TrackStartedDelegate(object sender, TrackStartedEventArgs args);

    public class TrackStartedEventArgs
    {
        public ISynoTrack Track { get; set; }
    }

    public delegate void TrackEndedDelegate(object sender, TrackEndedDelegateArgs args);

    public class TrackEndedDelegateArgs
    {
        public ISynoTrack Track { get; set; }
    }
}