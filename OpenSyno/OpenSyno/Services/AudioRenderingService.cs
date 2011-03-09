﻿using System.Windows.Data;

namespace OpenSyno.Services
{
    using System;
    using System.IO;
    using System.IO.IsolatedStorage;
    using System.Net;
    using System.Threading;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;

    using Media;

    using Synology.AudioStationApi;

    public class AudioRenderingService : IAudioRenderingService, IDisposable
    {
        private const string PositionPropertyName = "Position";
        private IAudioStationSession _audioStationSession;
        private MediaElement _mediaElement;

        /// <summary>
        /// A reference to the <see cref="SynoTrack"/> object representing the track being rendered.
        /// </summary>
        /// <remarks>
        /// Because we are using a private member, there can be only one track being played at a time, and therefore, this service is not thread-safe.
        /// </remarks>
        private SynoTrack _currentTrack;

        public event EventHandler<MediaPositionChangedEventArgs> MediaPositionChanged;

        public event EventHandler<MediaEndedEventArgs> MediaEnded;

        public AudioRenderingService(IAudioStationSession audioStationSession)
        {
            if (audioStationSession == null)
            {
                throw new ArgumentNullException("audioStationSession");
            }
            _audioStationSession = audioStationSession;

            _mediaElement = (MediaElement)App.Current.Resources["MediaElement"];

            BufferPlayableHeuristicPredicate = (track, bytesLoaded) => bytesLoaded >= track.Bitrate || bytesLoaded == track.Size;

            _mediaElement.SetBinding(MediaElement.PositionProperty, new Binding { Source = this, Mode = BindingMode.TwoWay, Path = new PropertyPath(PositionPropertyName)  });
            _mediaElement.CurrentStateChanged += OnCurrentStateChanged;
            _mediaElement.MediaOpened += MediaOpened;
            _mediaElement.MediaEnded += PlayingMediaEnded;
        }

        private TimeSpan _position;
        public TimeSpan Position
        {
            get { return _position; }
            set
            {
                _position = value;
                OnMediaPositionChanged(Position);
            }
        }

        private void OnMediaPositionChanged(TimeSpan position)
        {
            if (MediaPositionChanged != null)
            {
                MediaPositionChanged(this, new MediaPositionChangedEventArgs {Position = position, Duration = _mediaElement.NaturalDuration.TimeSpan});
            }
        }

        public Func<SynoTrack, long, bool> BufferPlayableHeuristicPredicate { get; set; }

        /// <summary>
        /// The heuristic used to define whether a given buffer can be played.
        /// </summary>
        /// <param name="track">The track being loaded.</param>
        /// <param name="loadedBytes">The amount of loaded bytes.</param>
        /// <returns></returns>
        /// <remarks>The method can be overrided, but the default predicate can also easily be replaced with the <see cref="BufferPlayableHeuristicPredicate"/> property.</remarks>
        public virtual bool BufferPlayableHeuristic(SynoTrack track, long loadedBytes)
        {
            return BufferPlayableHeuristicPredicate(track, loadedBytes);
        }

        /// <summary>
        /// Downloads to temporary file.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="contentLength">Length of the content.</param>
        /// <param name="targetStream">The target stream.</param>
        /// <param name="filePath">The file path.</param>
        /// <param name="bufferingProgressUpdate">The buffering progress update.</param>
        private void DownloadToTemporaryFile(Stream stream, long contentLength, Stream targetStream, SynoTrack synoTrack, string filePath, Action<double> bufferingProgressUpdate)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            if (targetStream == null)
            {
                throw new ArgumentNullException("targetStream");
            }

            int bufferSize = 1024;
            var buffer = new byte[bufferSize];

            stream.BeginRead(buffer, 0, buffer.Length, DownloadTrackCallback, new DownloadFileState(stream, contentLength, targetStream,synoTrack, buffer, filePath, stream.Length, bufferingProgressUpdate));          
        }



        public void DownloadTrackCallback(IAsyncResult ar)
        {
            var args = (DownloadFileState)ar.AsyncState;
            var sourceStream = args.SourceStream;
            var bytesLeft = args.BytesLeft;
            var targetStream = args.TargetStream;
            var buffer = args.Buffer;
            var filePath = args.FilePath;
            var readCount = sourceStream.EndRead(ar);
            long fileSize = args.FileSize;
            var synoTrack = args.SynoTrack;
            Action<double> bufferingProgressUpdate = args.BufferingProgressUpdate;
            bytesLeft = bytesLeft - readCount;

            // that probably means the stream has been closed by the media element before it had a chance to be transfered completely, so we'll abort the download.
            if (!targetStream.CanRead)
            {
                return;
            }
            lock (bufferingProgressUpdate)
            {
                var oldPosition = targetStream.Position;
                targetStream.Position = targetStream.Length;
                targetStream.Write(buffer, 0, readCount);
                targetStream.Position = oldPosition;
            }

            var bufferingProgressUpdatedEventArgs = new BufferingProgressUpdatedEventArgs { BytesLeft = bytesLeft, FileSize = fileSize, FileName = filePath, BufferingStream = targetStream, SynoTrack = synoTrack};
            Deployment.Current.Dispatcher.BeginInvoke(() =>
                OnBufferingProgressUpdated(bufferingProgressUpdatedEventArgs));

            if (bytesLeft > 0)
            {               
                // Not really fond of the idea of having a separate thread to do that, but it's fairly simple to implement... 
                // C#5 will definitely come handy here !
                // Plus, it doesn't force us to write it in an iterative way :
                // recursive is fine because the callstack will not get too big, since it's always on an other thread,
                // and the original one is then allowed to end properly.
                ThreadPool.QueueUserWorkItem(o => sourceStream.BeginRead(
                        buffer, 
                        0,
                        buffer.Length, 
                        DownloadTrackCallback,
                        new DownloadFileState(sourceStream, bytesLeft, targetStream,synoTrack, buffer, filePath, sourceStream.Length, bufferingProgressUpdate)));
            }
            else
            {
                sourceStream.Close();
                sourceStream.Dispose();                
            }

        }

        public event EventHandler<BufferingProgressUpdatedEventArgs> BufferingProgressUpdated;
        bool _isPlayable;

        private void OnBufferingProgressUpdated(BufferingProgressUpdatedEventArgs bufferingProgressUpdatedEventArgs)
        {
            if (BufferingProgressUpdated != null)
            {
                BufferingProgressUpdated(this, bufferingProgressUpdatedEventArgs);
            }

            if (_isPlayable || bufferingProgressUpdatedEventArgs.SynoTrack != _currentTrack)
            {
                return;
            }

            // FIXME : use event args, not instance fields, and maybe pass stream along or something similar to make sure we're comparing the right stream buffering progress
            _isPlayable = this.BufferPlayableHeuristic(bufferingProgressUpdatedEventArgs.SynoTrack, bufferingProgressUpdatedEventArgs.FileSize - bufferingProgressUpdatedEventArgs.BytesLeft);
            if (_isPlayable)
            {
                OnBufferReachedPlayableState(bufferingProgressUpdatedEventArgs.BufferingStream);
            }


        }

        private void OnCurrentStateChanged(object sender, RoutedEventArgs e)
        {
            //var state = ((MediaElement)sender).CurrentState;
        }


        private void PlayingMediaEnded(object sender, RoutedEventArgs e)
        {
            if (MediaEnded != null)
            {
                MediaEnded(this, new MediaEndedEventArgs { Track = _currentTrack });
            }
        }



        private void MediaOpened(object sender, RoutedEventArgs e)
        {
            // TODO : Start timer to update position every second.
        }

        internal class DownloadFileState
        {
            private readonly long _fileSize;

            public Stream SourceStream { get; set; }
            public long BytesLeft { get; set; }
            public Stream TargetStream { get; set; }

            public SynoTrack SynoTrack { get; set; }

            public byte[] Buffer { get; set; }
            public string FilePath { get; set; }
            public long FileSize { get; set; }
            public Action<double> BufferingProgressUpdate { get; set; }

            public DownloadFileState(Stream sourceStream, long bytesLeft, Stream targetStream,SynoTrack synoTrack, byte[] buffer, string filePath, long fileSize, Action<double> bufferingProgressUpdate)
            {                
                if (sourceStream == null)
                {
                    throw new ArgumentNullException("sourceStream");
                }

                if (targetStream == null)
                {
                    throw new ArgumentNullException("targetStream");
                }

                if (buffer == null)
                {
                    throw new ArgumentNullException("buffer");
                }
                if (filePath == null) throw new ArgumentNullException("filePath");
                if (bufferingProgressUpdate == null)
                {
                    throw new ArgumentNullException("bufferingProgressUpdate");
                }
                _fileSize = fileSize;
                SourceStream = sourceStream;
                BytesLeft = bytesLeft;
                TargetStream = targetStream;
                SynoTrack = synoTrack;
                Buffer = buffer;
                FilePath = filePath;
                FileSize = fileSize;
                BufferingProgressUpdate = bufferingProgressUpdate;
            }
        }

        public void Bufferize(Action<Stream> bufferizedCallback, Action<double> bufferizeProgressChangedCallback , SynoTrack track)
        {            
            if (bufferizedCallback == null)
            {
                throw new ArgumentNullException("bufferizedCallback");
            }
            if (bufferizeProgressChangedCallback == null)
            {
                throw new ArgumentNullException("bufferizeProgressChangedCallback");
            }
            if (track == null)
            {
                throw new ArgumentNullException("track");
            }

            _currentTrack = track;


            _audioStationSession.GetFileStream(track, OnFileStreamOpened);

            // TODO : Start download
        }

        private void OnFileStreamOpened(WebResponse response, SynoTrack synoTrack)
        {          
            var trackStream = response.GetResponseStream();

            // The trackstream is not readable.
            _isPlayable = false;

            Stream targetStream = new MemoryStream((int)trackStream.Length);

            DownloadToTemporaryFile(trackStream, response.ContentLength, targetStream, synoTrack, string.Empty, BufferedProgressUpdated);
            
        }

        private void BufferedProgressUpdated(double obj)
        {
           
        }

        private void OnBufferReachedPlayableState(Stream stream)
        {
            // Hack : for now we just avoid it to crash : it seems that not continuing the download is not enough since an pother thread might still be running
            // and continuing once too much and make everything crash : 
            // here, by fixing this that way, we'll have some side effects : the download will continue and the download progress bar will show alternatively both statuses... 
            // we definitely must fix this an other way :)
            if (stream.CanRead)
            {
                Delegate mediaRenderingStarter = new Action<Stream>(streamToPlay =>
                                                                          {
                                                                             
                                                                             _mediaElement.Stop();
                                                                             Mp3MediaStreamSource mss = new Mp3MediaStreamSource(streamToPlay);
                                                                             _mediaElement.SetSource(mss);

                                                                             _mediaElement.Position = TimeSpan.FromSeconds(0);
                                                                             _mediaElement.Volume = 20;
                                                                             _mediaElement.Play();           
                                                                         });
                Deployment.Current.Dispatcher.BeginInvoke(mediaRenderingStarter, new object[] {stream});
            }
        }

        public void Play(Stream trackStream)
        {
            // TODO : ask the media element to start playing.
            _mediaElement.Stop();
            MediaStreamSource mms = new Mp3MediaStreamSource(trackStream);
            _mediaElement.SetSource(mms);

            _mediaElement.Position = TimeSpan.FromSeconds(0);
            _mediaElement.Volume = 20;
            _mediaElement.Play();
        }

        public void Dispose()
        {            
            _mediaElement.CurrentStateChanged -= OnCurrentStateChanged;
            _mediaElement.MediaOpened -= MediaOpened;
            _mediaElement.MediaEnded -= PlayingMediaEnded;
        }
    }

    public class BufferingProgressUpdatedEventArgs : EventArgs
    {
        public long BytesLeft { get; set; }

        public long FileSize { get; set; }

        public string FileName { get; set; }

        public Stream BufferingStream { get; set; }

        public SynoTrack SynoTrack { get; set; }
    }

    public class MediaPositionChangedEventArgs : EventArgs
    {
        public TimeSpan Position { get; set; }

        public TimeSpan Duration { get; set; }        
    }
}