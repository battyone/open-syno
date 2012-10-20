﻿using Ninject;
using OpemSyno.Contracts;

namespace OpenSyno.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.IsolatedStorage;
    using System.Linq;
    using System.Net;
    using System.Runtime.Serialization;
    using System.Threading;
    using System.Windows;
    using Microsoft.Phone.BackgroundAudio;

    using OpenSyno.BackgroundPlaybackAgent;
    using OpenSyno.Contracts.Domain;

    using Synology.AudioStationApi;

    public class PlaybackService : IPlaybackService
    {
        /// <summary>
        /// The service responsible for downloading and rendering the audio files.
        /// </summary>

        private readonly IAudioStationSession _audioStationSession;

        private readonly IAudioRenderingServiceFactory _audioRenderingServiceFactory;

        private readonly IAudioRenderingService _audioRenderingService;

        private readonly IAudioTrackFactory _audioTrackFactory;

        private List<GuidToTrackMapping> _tracksToGuidMapping;

        private List<AsciiUriFix> _asciiUriFixes;

        // HACK : remove this and use the duration of the media stream source
        /// <summary>
        /// the last started track
        /// </summary>
        /// <remarks>The MP3 Media Stream Source is buggy with Variable bitrates, so the duration is not computed correctly. Instead, we want to use the duration exposed by Synology. In order to do this, we need to keep a reference on the last played SynoTrack.</remarks>
        private SynoTrack _lastStartedTrack;

        private ILogService _logService;

        private Dictionary<Guid, SynoTrack> _cachedAudioTracks;

        private Timer _progressUpdater;
        private IVersionDependentResourcesProvider _versionDependentResourceProvider;

        /// <summary>
        /// Gets or sets what strategy should be used to define the next track to play.
        /// </summary>
        /// <value>The playback continuity.</value>
        public PlaybackContinuity PlaybackContinuity { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the next track to be played should be preloaded.
        /// </summary>
        /// <value><c>true</c> if the next track to be played should be preloaded; otherwise, <c>false</c>.</value>
        public bool PreloadTracks { get; set; }

        ///// <summary>
        ///// Gets the items in the playqueue.
        ///// </summary>
        ///// <value>The items in the playqueue.</value>        
        //public ObservableCollection<ISynoTrack> PlayqueueItems { get; private set; }

        

        public PlayState Status
        {
            get
            {
                return BackgroundAudioPlayer.Instance.PlayerState;
            }
        }

        ///// <summary>
        ///// Clears the play queue.
        ///// </summary>
        //public void ClearPlayQueue()
        //{
        //    PlayqueueItems.Clear();
        //}

        ///// <summary>
        ///// Inserts the specified tracks to the play queue.
        ///// </summary>
        ///// <param name="tracks">The tracks.</param>
        ///// <param name="insertPosition">The position in the play queue where to insert the specified tracks.</param>
        //public void InsertTracksToQueue(IEnumerable<ISynoTrack> tracks, int insertPosition)
        //{
        //    foreach (var synoTrack in tracks)
        //    {
        //        PlayqueueItems.Insert(insertPosition, synoTrack);
        //        insertPosition++;
        //    }
        //}

        /// <summary>
        /// Plays the specified track. It must be present in the queue.
        /// </summary>
        /// <param name="trackToPlay">The track to play.</param>
        public void PlayTrackInQueue(Guid trackToPlay)
        {
            _audioRenderingService.StreamTrack(trackToPlay);            
        }

        protected void OnTrackStarted(TrackStartedEventArgs trackStartedEventArgs)
        {
            _logService.Trace("PlaybackService.OnTrackStarted : " + trackStartedEventArgs.Track.Title);
            _lastStartedTrack = trackStartedEventArgs.Track;
            if (TrackStarted != null)
            {
                TrackStarted(this, trackStartedEventArgs);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PlaybackService"/> class.
        /// </summary>
        /// <param name="audioStationSession"></param>
        /// <param name="audioTrackFactory"></param>
        public PlaybackService(IAudioStationSession audioStationSession,IAudioRenderingServiceFactory audioRenderingServiceFactory, IAudioTrackFactory audioTrackFactory, IVersionDependentResourcesProvider versionDependentResourceProvider)
        {
            _logService = IoC.Container.Get<ILogService>();
                        
            _audioStationSession = audioStationSession;
            _audioRenderingServiceFactory = audioRenderingServiceFactory;
            _audioTrackFactory = audioTrackFactory;
            _versionDependentResourceProvider = versionDependentResourceProvider;

            
            // We need an observable collection so we can serialize the items to IsolatedStorage in order to get the background rendering service to read it from disk, since the background Agent is not running in the same process.
            //PlayqueueItems = new ObservableCollection<ISynoTrack>();

            this._tracksToGuidMapping = new List<GuidToTrackMapping>();

            using (var userStoreForApplication = IsolatedStorageFile.GetUserStoreForApplication())
            {

                using (
                    IsolatedStorageFileStream asciiUriFixes = userStoreForApplication.OpenFile(
                        "AsciiUriFixes.xml", FileMode.OpenOrCreate))
                {

                    DataContractSerializer dcs = new DataContractSerializer(typeof(List<AsciiUriFix>));
                    //var xs = new XmlSerializer(typeof(PlayqueueInterProcessCommunicationTransporter));

                    try
                    {
                        _asciiUriFixes = (List<AsciiUriFix>)dcs.ReadObject(asciiUriFixes);
                    }
                    catch (Exception e)
                    {
                        // could not deserialize XML for playlist : let's build an empty list.
                        _logService.Error("could not deserialize XML for playlist : let's build an empty list.");
                        _asciiUriFixes = new List<AsciiUriFix>();
                    }
                }

                PlayqueueInterProcessCommunicationTransporter deserialization = null;
                using (
                    IsolatedStorageFileStream playQueueFile = userStoreForApplication.OpenFile(
                        "playqueue.xml", FileMode.OpenOrCreate))
                {

                    DataContractSerializer dcs =
                        new DataContractSerializer(typeof(PlayqueueInterProcessCommunicationTransporter));
                    //var xs = new XmlSerializer(typeof(PlayqueueInterProcessCommunicationTransporter));

                    try
                    {
                        deserialization = (PlayqueueInterProcessCommunicationTransporter)dcs.ReadObject(playQueueFile);

                        foreach (GuidToTrackMapping pair in deserialization.Mappings)
                        {
                            this._tracksToGuidMapping.Add(pair);
                        }
                    }
                    catch (Exception e)
                    {
                        // could not deserialize XML for playlist : let's keep it empty.

                    }
                }
            }


            this._audioRenderingService = this._audioRenderingServiceFactory.Create(this._tracksToGuidMapping, _asciiUriFixes);
            //this.PlayqueueItems.CollectionChanged += this.OnPlayqueueItemsChanged;

            BackgroundAudioPlayer.Instance.PlayStateChanged += new EventHandler(this.BackgroundPlayerPlayStateChanged);

            this._progressUpdater = new Timer(
                e =>
                {
                    var backgroundAudioPlayer = BackgroundAudioPlayer.Instance;
                    if (this.TrackCurrentPositionChanged != null && backgroundAudioPlayer.Track != null)
                    {

                        Deployment.Current.Dispatcher.BeginInvoke(() =>
                            {
                                TrackCurrentPositionChangedEventArgs trackCurrentPositionChangedEventArgs = new TrackCurrentPositionChangedEventArgs();

                                trackCurrentPositionChangedEventArgs.LoadPercentComplete = backgroundAudioPlayer.BufferingProgress;
                                double totalSeconds = 0;
                                TimeSpan position = new TimeSpan();

                                try
                                {
                                    position = backgroundAudioPlayer.Position;
                                }
                                catch (SystemException)
                                {
                                    _progressUpdater.Change(-1, 0);
                                    // swallow exception : we get an HRESULT error, when no valid position could be retrieved. Maybe a beta behavior that will change in the future. since we can ignore the error and set the duration to 0 ( maybe the track hasn't been loaded yet ) we'll just swallow the exception.
                                }

                                try
                                {
                                    totalSeconds = backgroundAudioPlayer.Position.TotalSeconds;
                                }
                                catch (SystemException)
                                {
                                    // swallow exception : we get an HRESULT error, when no valid duration could be retrieved. Maybe a beta behavior that will change in the future. since we can ignore the error and set the duration to 0 ( maybe the track hasn't been loaded yet ) we'll just swallow the exception.
                                }

                                if (position.TotalSeconds == 0)
                                {
                                    // avoid zero-division
                                    trackCurrentPositionChangedEventArgs.PlaybackPercentComplete = 0;
                                }
                                else
                                {
                                    trackCurrentPositionChangedEventArgs.PlaybackPercentComplete = totalSeconds / backgroundAudioPlayer.Track.Duration.TotalSeconds;
                                    trackCurrentPositionChangedEventArgs.Position = position;
                                }

                                this.TrackCurrentPositionChanged(this, trackCurrentPositionChangedEventArgs);
                            });
                    }
                },
                null,
                0,
                200);
            //(o, e) =>
            //{
            //    _backgroundAudioRenderingService.OnPlayqueueItemsChanged(e.NewItems, e.OldItems);
            //};
        }

        private void BackgroundPlayerPlayStateChanged(object sender, EventArgs e)
        {
            PlayState playerState = BackgroundAudioPlayer.Instance.PlayerState;
            _logService.Trace("Background player state changed : " + playerState);
            Guid guid = Guid.Empty;
            string source = string.Empty;

            if (BackgroundAudioPlayer.Instance.Track != null)
            {
                guid = Guid.Parse(BackgroundAudioPlayer.Instance.Track.Tag);
                source = BackgroundAudioPlayer.Instance.Track.Source.ToString();
                _logService.Trace("Current track guid : " + guid);
                _logService.Trace("Current track source : '" + source + "'");
            }
            else
            {
                _logService.Trace("Current track is null");
            }


            switch (playerState)
            {
                case PlayState.Unknown:
                    break;
                case PlayState.Stopped:
                    break;
                case PlayState.Paused:
                    break;
                case PlayState.Playing:
                    if (_tracksToGuidMapping.Any(o=>o.Guid == guid))
                    {
                        SynoTrack synoTrack = this._tracksToGuidMapping.Single(o=>o.Guid == guid).Track;
                        _logService.Trace("Track matching the guid was found : " + synoTrack.Title);
                        OnTrackStarted(new TrackStartedEventArgs { Guid = guid, Track = synoTrack });
                    }
                    else
                    {
                        _logService.Trace("No track matching the guid was found. The play queue might have been deleted while a track was playing. The current track could not be identified.");
                    }

                    break;
                case PlayState.BufferingStarted:
                    break;
                case PlayState.BufferingStopped:
                    break;
                case PlayState.TrackReady:
                    break;
                case PlayState.TrackEnded:
                    break;
                case PlayState.Rewinding:
                    break;
                case PlayState.FastForwarding:
                    break;
                case PlayState.Shutdown:
                    break;
                case PlayState.Error:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
     

        //public ISynoTrack GetNextTrack(ISynoTrack currentTrack)
        //{
        //    int currentTrackIndex = PlayqueueItems.IndexOf(currentTrack);

        //    if (PlayqueueItems.Count > currentTrackIndex + 1)
        //    {
        //        return PlayqueueItems[currentTrackIndex + 1];
        //    }

        //    // if there is no track next, then we return null.
        //    return null;
        //}

        public void PausePlayback()
        {
            _audioRenderingService.Pause();
        }

        public void ResumePlayback()
        {
            _audioRenderingService.Resume();
        }

        public double GetVolume()
        {
            return _audioRenderingService.GetVolume();
        }

        public void SetVolume(double volume)
        {
            _audioRenderingService.SetVolume(volume);
        }


        public void SkipNext()
        {
            // here, we should implement the logic to find the next track to play, but since the bachgroundAudioRenderingService exposes such features, we'll rely on them.
            // remember though that it makes the PlaybackService tightly coupled with the BackgroundAudioRenderingService.
            // or maybe we should merge those two services for this particular implementation of PlaybackService.
            BackgroundAudioPlayer.Instance.SkipNext();
            //this._backgroundAudioRenderingService.SkipNext();
        }

        public event PlayqueueChangedEventHandler PlayqueueChanged;

        public void InsertTracksToQueue(IEnumerable<GuidToTrackMapping> tracksToGuidMapping, int insertPosition, Action<Dictionary<SynoTrack, Guid>> callback)
        {
            // TODO : Code deserves a bit of cleanup and a few optimizations : LINQ has been clearly blindly abused here... we are most certainly not doing it in the simplest / shortest way ! apologies !

            if (tracksToGuidMapping == null) throw new ArgumentNullException("tracksToGuidMapping");


            if (insertPosition != _tracksToGuidMapping.Count())
            {
                // we need to change the infrastructure and replace the dictionary with a list so we can choose the order !
                throw new NotSupportedException("Can only insert tracks at the end of the queue for now. sorry :(");
            }
            var i = 0;
            bool isAnyFixNeeded = false;
            // FIXME : Be able to choose the position
            foreach (var guidToTrackMapping in tracksToGuidMapping)
            {
                // create new Guid if current is empty.
                Guid trackGuid = Guid.NewGuid();

                _tracksToGuidMapping.Add(new GuidToTrackMapping(trackGuid, guidToTrackMapping.Track));
                var tracksToFix = _tracksToGuidMapping.Where(mapping => _asciiUriFixes.All(fix => mapping.Track.Res != fix.Res) && mapping.Track.Res.Any(c => c == '&' || c > 127)).Select(t => t.Track);
                isAnyFixNeeded = tracksToFix.Any();
                foreach (var track in tracksToFix)
                {
                    _asciiUriFixes.Add(new AsciiUriFix(track.Res, null));

                    // query shorten URL

                    // Use url-shortening service.
                    // http://t0.tv/api/shorten?u=<url>
                    WebClient webClient = new WebClient();


                    webClient.DownloadStringCompleted += (s, e) =>
                    {
                        var shortUrl = e.Result;
                        var res = (string)e.UserState;
                        _asciiUriFixes.Single(fix => fix.Res == res).Url = shortUrl;
                        if (!_asciiUriFixes.Any(fix => fix.Url == null))
                        {
                            SerializeAsciiUriFixes();
                            Dictionary<SynoTrack, Guid> dictionary = _tracksToGuidMapping.Where(o => _tracksToGuidMapping.Any(m => m.Track == o.Track)).ToDictionary(o => o.Track, o => o.Guid);
                            callback(dictionary);
                        }
                    };
                    var relativePathToAudioStreamService = this._versionDependentResourceProvider.GetAudioStreamWebserviceRelativePath(DsmVersions.V4_0);
                    string url =
                   string.Format(
                       "http://{0}:{1}{2}/0.mp3?sid={3}&action=streaming&songpath={4}",
                       _audioStationSession.Host,
                       _audioStationSession.Port,
                       relativePathToAudioStreamService,
                       _audioStationSession.Token.Split('=')[1],
                       HttpUtility.UrlEncode(track.Res).Replace("+", "%20"));

                    webClient.DownloadStringAsync(new Uri("http://tinyurl.com/api-create.php?url=" + HttpUtility.UrlEncode(url)), track.Res);

                }
                // FIXME : Urgent : replace NotifyCollectionChanged by a custom event that can propagate bulk collection changes ! (optimize writes on disk )
                //OnTracksInQueueChanged(new PlayqueueChangedEventArgs { AddedItems = new[] { new GuidToTrackMapping(trackGuid, guidToTrackMapping.Track) }, AddedItemsPosition = insertPosition + i });
                i++;
            }

            // if no fix was needed, the callback hasn't been called yet !
            if (!isAnyFixNeeded)
            {
                Dictionary<SynoTrack, Guid> dictionary = new Dictionary<SynoTrack, Guid>();

                foreach (var mapping in tracksToGuidMapping)
                {
                    dictionary.Add(mapping.Track, _tracksToGuidMapping.Where(o => o.Track == mapping.Track).First().Guid);
                }

                callback(dictionary);
            }

            var newItemsWithGuid = _tracksToGuidMapping.Where(m => tracksToGuidMapping.Any(o => o.Track == m.Track));

            OnTracksInQueueChanged(new PlayqueueChangedEventArgs { AddedItems = newItemsWithGuid, AddedItemsPosition = insertPosition });

        }

        public void SerializeAsciiUriFixes()
        {
            using (var userStore = IsolatedStorageFile.GetUserStoreForApplication())
            {
                using (IsolatedStorageFileStream stream = userStore.OpenFile("AsciiUriFixes.xml", FileMode.Create))
                {
                    var dcs = new DataContractSerializer(typeof(List<AsciiUriFix>));
                    dcs.WriteObject(stream, _asciiUriFixes);
                }    
            }            
        }
        
        public void RefreshTracksInQueue()
        {

        }

        private void OnTracksInQueueChanged(PlayqueueChangedEventArgs eventArgs)
        {
            if (eventArgs == null)
            {
                throw new ArgumentNullException("eventArgs");
            }

            SerializePlayqueue();

            if (PlayqueueChanged != null)
            {
                PlayqueueChanged(this, eventArgs);
            }


        }

        private void SerializePlayqueue()
        {
            _logService.Trace("Serializing playqueue");
            // TODO : Handle case where the background agent would be reading and the file cannot be written to !
            bool tryAgain = true;
            int triesCount = 1;
            while (tryAgain)
            {
                try
                {
                    using (IsolatedStorageFile userStoreForApplication = IsolatedStorageFile.GetUserStoreForApplication())
                    {
                        using (IsolatedStorageFileStream playQueueFile = userStoreForApplication.OpenFile("playqueue.xml", FileMode.Create))
                        {
                            var dcs = new DataContractSerializer(typeof(PlayqueueInterProcessCommunicationTransporter));

                            var serialization = new PlayqueueInterProcessCommunicationTransporter()
                            {
                                Host = _audioStationSession.Host,
                                Port = _audioStationSession.Port,
                                Mappings = _tracksToGuidMapping,
                                Token = _audioStationSession.Token
                            };
                            dcs.WriteObject(playQueueFile, serialization);
                        }
                    }
                    
                    tryAgain = false;
                }
                catch (Exception e)
                {
                    _logService.Error("Reading in the isolated storage failed. Will try 10 times before giving up.");
                    Thread.Sleep(300);
                    triesCount++;
                    if (triesCount > 10)
                    {
                        tryAgain = false;
                        throw new InvalidOperationException("The isolated storage could ont be accessed at this time.", e);
                    }
                    throw;
                }
                
            }            
        }

        public IEnumerable<GuidToTrackMapping> GetTracksInQueue()
        {
            return _tracksToGuidMapping;
        }



        public GuidToTrackMapping GetCurrentTrack()
        {
            var audioTrack = BackgroundAudioPlayer.Instance.Track;
            if (audioTrack == null || !_tracksToGuidMapping.Any(o=>o.Guid == Guid.Parse(audioTrack.Tag)))
            {
                return null;
            }

            Guid guid = Guid.Parse(audioTrack.Tag);
            return new GuidToTrackMapping(guid, _tracksToGuidMapping.Single( o => o.Guid == guid).Track);
        }

        public void RemoveTracksFromQueue(IEnumerable<Guid> tracksToRemove)
        {
            // If not using an array, an Invalid Operation Exception will be thrown : something about Movenext, maybe the enumerator is not correctly implemented.
            var trackToRemoveArray = tracksToRemove.ToArray();
            PlayqueueChangedEventArgs ea = new PlayqueueChangedEventArgs();
            var guidToTrackMappings = new GuidToTrackMapping[trackToRemoveArray.Length];
            int guidToTrackMappingIndex = 0;
            foreach (var guid in trackToRemoveArray)
            {

                var guidToTrackMapping = _tracksToGuidMapping.Single(o => o.Guid == guid);


                _tracksToGuidMapping.Remove(guidToTrackMapping);

                guidToTrackMappings[guidToTrackMappingIndex] = guidToTrackMapping;
                guidToTrackMappingIndex++;
            }
            ea.RemovedItems = guidToTrackMappings;
            this.OnTracksInQueueChanged(ea);
        }

        public void SkipPrevious()
        {
            BackgroundAudioPlayer.Instance.SkipPrevious();
        }

        /// <summary>
        /// Invalidates the cached tokens.
        /// </summary>
        /// <remarks>Used to make sure we purge the fix for the uri containing non-ascii7 characters. The current architecture relies on the fact that we only change the token if the cached token is outdated. fixes use url-shortening services and have their token enclosed, so it is necessary not to update the cached token indepedently from the cached uri fixes !</remarks>
        public void InvalidateCachedTokens()
        {
            _logService.Trace("PlaybackService : Invalidating cached token");
            RemoveRemoteLocations();


            DetectAffectedTracksAndBuildFix(_tracksToGuidMapping, mapping => SerializeAsciiUriFixes());
            SerializePlayqueue();
        }

        public void RemoveUriRedirections(IEnumerable<AsciiUriFix> redirectionsToRemove)
        {
            foreach (var asciiUriFix in redirectionsToRemove)
            {
                _asciiUriFixes.Remove(asciiUriFix);
            }
        }

        public void RemoveRemoteLocations()
        {
            //_asciiUriFixes.Clear();
            IEnumerable<AsciiUriFix> itemsToRemove;
            using (var userStore = IsolatedStorageFile.GetUserStoreForApplication())
            {
                // Don't forget the ToArray, otherwise, linq will defer the iteration after the disposal of the userStore.
                itemsToRemove = _asciiUriFixes.Where(o => !userStore.FileExists(o.Url)).ToArray();
            }
            RemoveUriRedirections(itemsToRemove);
        }

        public void ClearTracksInQueue()
        {            
            var mappingsCopy = _tracksToGuidMapping.ToArray();
            _tracksToGuidMapping.Clear();

            if (mappingsCopy.Length > 0)
            {
                OnTracksInQueueChanged(new PlayqueueChangedEventArgs { RemovedItems = mappingsCopy });
            }
        }

        public int GetTracksCountInQueue()
        {
            return _tracksToGuidMapping.Count();
        }

        /// <summary>
        /// Detects the affected tracks and build a fix.
        /// </summary>
        /// <param name="potentiallyUnsafeMappings">The potentially unsafe mappings.</param>
        private void DetectAffectedTracksAndBuildFix(List<GuidToTrackMapping> potentiallyUnsafeMappings, Action<Dictionary<SynoTrack, Guid>> callback)
        { 
            var tracksToFix = _tracksToGuidMapping.Where(mapping => !_asciiUriFixes.Any(fix => mapping.Track.Res == fix.Res) && mapping.Track.Res.Any(c => c == '&' || c > 127)).Select(t => t.Track);
            foreach (var track in tracksToFix)
            {
                AddUrlRedirection(track, null);

                // query shorten URL

                // Use url-shortening service.
                // http://t0.tv/api/shorten?u=<url>
                WebClient webClient = new WebClient();


                webClient.DownloadStringCompleted += (s, e) =>
                {
                    var shortUrl = e.Result;
                    var res = (string)e.UserState;
                    _asciiUriFixes.Where(fix => fix.Res == res).Single().Url = shortUrl;
                    if (!_asciiUriFixes.Any(fix => fix.Url == null))
                    {                        
                        callback(_tracksToGuidMapping.Where(o => potentiallyUnsafeMappings.Any(x => x.Track == o.Track)).ToDictionary(o => o.Track, o => o.Guid));
                    }
                };
                var relativePathToAudioStreamService = this._versionDependentResourceProvider.GetAudioStreamWebserviceRelativePath(DsmVersions.V4_0);
                string url =
                string.Format(
                    "http://{0}:{1}{2}/0.mp3?sid={3}&action=streaming&songpath={4}",
                    _audioStationSession.Host,
                    _audioStationSession.Port,
                    relativePathToAudioStreamService,
                    _audioStationSession.Token.Split('=')[1],
                    HttpUtility.UrlEncode(track.Res).Replace("+", "%20"));

                webClient.DownloadStringAsync(new Uri("http://tinyurl.com/api-create.php?url=" + HttpUtility.UrlEncode(url)), track.Res);

            }
        }

        /// <summary>
        /// Adds an URL redirection.
        /// </summary>
        /// <remarks>Typically used for URL that need ASCII fix, but also for cached tracks.</remarks>
        /// <param name="track">The track.</param>
        public void AddUrlRedirection(SynoTrack track, string url)
        {
            AsciiUriFix existingRedirection = _asciiUriFixes.FirstOrDefault(o => o.Res == track.Res);
            if (existingRedirection != null)
            {
                existingRedirection.Url = url;
            }
            else
            {
                _asciiUriFixes.Add(new AsciiUriFix(track.Res, url));
            }
        }        

        public string GetRedirectionForTrack(SynoTrack trackInfo)
        {
            var firstOrDefault = _asciiUriFixes.FirstOrDefault(o => o.Res == trackInfo.Res);
            if (firstOrDefault != null)
            {
                var redirectionForTrack = firstOrDefault.Url;
                return redirectionForTrack;
            }
            else
            {
                return null;
            }
        }

        private void OnMediaPositionChanged(object sender, MediaPositionChangedEventArgs e)
        {
            OnTrackCurrentPositionChanged(e);
        }

        private void OnTrackCurrentPositionChanged(MediaPositionChangedEventArgs mediaPositionChangedEventArgs)
        {
            if (TrackCurrentPositionChanged != null)
            {
                // Hack : we shouldn't have to resort to use a SynoTrack, but since the MediaStreamSource is buggy, we don't have much of a choice...
                TrackCurrentPositionChanged(this, new TrackCurrentPositionChangedEventArgs { LoadPercentComplete = 1, PlaybackPercentComplete = mediaPositionChangedEventArgs.Position.TotalSeconds / _lastStartedTrack.Duration.TotalSeconds, Position = mediaPositionChangedEventArgs.Position });
                //TrackCurrentPositionChanged(this, new TrackCurrentPositionChangedEventArgs { LoadPercentComplete = 1, PlaybackPercentComplete = mediaPositionChangedEventArgs.Position.TotalSeconds / mediaPositionChangedEventArgs.Duration.TotalSeconds, Position = mediaPositionChangedEventArgs.Position });
            }
        }

        public event TrackEndedDelegate TrackEnded;
        public event TrackStartedDelegate TrackStarted;
        public event TrackCurrentPositionChangedDelegate TrackCurrentPositionChanged;

    }

    public interface IAudioRenderingServiceFactory
    {
        IAudioRenderingService Create(List<GuidToTrackMapping> tracksToGuidMapping, List<AsciiUriFix> asciiUriFixes);
    }

    public class AudioRenderingServiceFactory : IAudioRenderingServiceFactory
    {
        private ILogService logService;
        private IAudioTrackFactory audioTrackFactory;
        private IAudioStationSession audioStationSession;

        public AudioRenderingServiceFactory(ILogService logService, IAudioStationSession audioStationSession, IAudioTrackFactory audioTrackFactory)
        {
            this.audioStationSession = audioStationSession;
            this.logService = logService;
            this.audioTrackFactory = audioTrackFactory;
        }

        public IAudioRenderingService Create(List<GuidToTrackMapping> tracksToGuidMapping, List<AsciiUriFix> asciiUriFixes)
        {
            return new AudioRenderingService(tracksToGuidMapping, asciiUriFixes, logService, audioStationSession, audioTrackFactory);
        }
    }

    public class AudioRenderingService : IAudioRenderingService
    {
        private readonly List<GuidToTrackMapping> _tracksToGuidMapping;
        private readonly List<AsciiUriFix> _asciiUriFixes;
        private readonly ILogService _logService;
        private IAudioTrackFactory _audioTrackFactory;
        private IAudioStationSession _audioStationSession;

        public AudioRenderingService(List<GuidToTrackMapping> tracksToGuidMapping, List<AsciiUriFix> asciiUriFixes, ILogService logService, IAudioStationSession audioStationSession, IAudioTrackFactory audioTrackFactory)
        {
            _tracksToGuidMapping = tracksToGuidMapping;
            _asciiUriFixes = asciiUriFixes;
            _logService = logService;
            _audioStationSession = audioStationSession;
            _audioTrackFactory = audioTrackFactory;
        }

        public event EventHandler<MediaPositionChangedEventArgs> MediaPositionChanged;
        public event EventHandler<MediaEndedEventArgs> MediaEnded;
        public event EventHandler<BufferingProgressUpdatedEventArgs> BufferingProgressUpdated;
        public event EventHandler<PlayBackStartedEventArgs> PlaybackStarted;
        #region audioservice

        public void Pause()
        {
            BackgroundAudioPlayer.Instance.Pause();
        }

        public void Resume()
        {
            BackgroundAudioPlayer.Instance.Play();
        }

        public double GetVolume()
        {
            return BackgroundAudioPlayer.Instance.Volume;
        }

        public void SetVolume(double volume)
        {
            if (BackgroundAudioPlayer.Instance.Volume != volume)
            {
                BackgroundAudioPlayer.Instance.Volume = volume;
            }
        }

        public void StreamTrack(Guid guidOfTrackToPlay)
        {
            _logService.Trace("Starting track streaming for track guid " + guidOfTrackToPlay);
            //// hack : Synology's webserver doesn't accept the + character as a space : it needs a %20, and it needs to have special characters such as '&' to be encoded with %20 as well, so an HtmlEncode is not an option, since even if a space would be encoded properly, an ampersand (&) would be translated into &amp;
            //string url =
            //    string.Format(
            //        "http://{0}:{1}/audio/webUI/audio_stream.cgi/0.mp3?sid={2}&action=streaming&songpath={3}",
            //        _audioStationSession.Host,
            //        _audioStationSession.Port,
            //        _audioStationSession.Token.Split('=')[1],
            //        HttpUtility.UrlEncode(trackToPlay.Res).Replace("+", "%20"));
            SynoTrack baseSynoTrack = _tracksToGuidMapping.Single(o => o.Guid == guidOfTrackToPlay).Track;
            AudioTrack audioTrack;
            if (_asciiUriFixes.Any(fix => fix.Res == baseSynoTrack.Res))
            {
                _logService.Trace("Uri fix found for track " + guidOfTrackToPlay);
                AsciiUriFix asciiUriFix = this._asciiUriFixes.Single(fix => fix.Res == baseSynoTrack.Res);
                asciiUriFix.CallbackWhenFixIsApplicable(fix =>
                {
                    audioTrack = _audioTrackFactory.Create(baseSynoTrack, guidOfTrackToPlay, _audioStationSession.Host, _audioStationSession.Port, _audioStationSession.Token, asciiUriFix.Url);
                    BackgroundAudioPlayer.Instance.Track = audioTrack;
                    this.Resume();
                    //BackgroundAudioPlayer.Instance.Play();
                });
            }
            else
            {
                _logService.Trace("No uri fix found for track " + guidOfTrackToPlay);
                audioTrack = _audioTrackFactory.Create(baseSynoTrack, guidOfTrackToPlay, _audioStationSession.Host, _audioStationSession.Port, _audioStationSession.Token);
                BackgroundAudioPlayer.Instance.Track = audioTrack;
                this.Resume();
                //BackgroundAudioPlayer.Instance.Play();
            }

        }



        #endregion
    }
}
