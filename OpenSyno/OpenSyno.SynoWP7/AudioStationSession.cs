﻿using Newtonsoft.Json.Linq;

namespace Synology.AudioStationApi
{
    using System.Runtime.Serialization;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using System.Windows;

    using Newtonsoft.Json;

    using OpenSyno.SynoWP7;

    [DataContract]
    public class AudioStationSession : IAudioStationSession
    {
        private IVersionDependentResourcesProvider versionDependentResourcesProvider;

        public AudioStationSession(IVersionDependentResourcesProvider versionDependentResourcesProvider, DsmVersions dsmVersion) : this()
        {
            this.DsmVersion = dsmVersion;
            this.versionDependentResourcesProvider = versionDependentResourcesProvider;
        }

        public AudioStationSession()
        {
            // TODO: Complete member initialization
        }

        [DataMember]
        public string Host { get;  set; }

        [DataMember]
        public int Port { get;  set; }

        [DataMember]
        public string Token { get; set; }

        [DataMember]
        public DsmVersions DsmVersion { get; set; }

        public Task<IEnumerable<SynoItem>> SearchArtistAsync(string artistName)
        {

            TaskCompletionSource<IEnumerable<SynoItem>> tcs = new TaskCompletionSource<IEnumerable<SynoItem>>();

            string urlBase = string.Format("http://{0}:{1}", this.Host, this.Port);
            //var url = urlBase + "/webman/modules/AudioStation/webUI/audio_browse.cgi";
            var url = urlBase + this.versionDependentResourcesProvider.GetAudioSearchWebserviceRelativePath(this.DsmVersion); 

            HttpWebRequest request = BuildRequest(url);

            int limit = 100;
            string postString = string.Format(@"sort=title&dir=ASC&action=browse&target=musiclib_music_aa&server=musiclib_music_aa&category=&keyword={0}&start=0&limit={1}", artistName, limit);
                       
            byte[] postBytes = System.Text.Encoding.UTF8.GetBytes(postString);

            var requestStreamAr = request.BeginGetRequestStream(ar =>
            {
                // Just make sure we retrieve the right web request : no access to modified closure.
                HttpWebRequest webRequest = (HttpWebRequest)ar.AsyncState;

                var requestStream = webRequest.EndGetRequestStream(ar);
                requestStream.Write(postBytes, 0, postBytes.Length);
                requestStream.Close();

                var getResponseAr = webRequest.BeginGetResponse(
                    responseAr =>
                    {
                        // Just make sure we retrieve the right web request : no access to modified closure.                        
                        var httpWebRequest = responseAr.AsyncState;

                        var webResponse = webRequest.EndGetResponse(responseAr);
                        var responseStream = webResponse.GetResponseStream();
                        var reader = new StreamReader(responseStream);
                        var content = reader.ReadToEnd();

                        long count;
                        IEnumerable<SynoItem> tracks;
                        SynologyJsonDeserializationHelper.ParseSynologyAlbums(
                            content, out tracks, out count, urlBase);

                        var isOnUiThread = Deployment.Current.Dispatcher.CheckAccess();
                        if (isOnUiThread)
                        {
                            if (count > limit)
                            {
                                // MessageBox.Show(string.Format("number of available artists ({0}) exceeds supported limit ({1})", count, limit));
                            }

                            tcs.SetResult(tracks);

                            // callback(tracks);
                        }
                        else
                        {
                            Deployment.Current.Dispatcher.BeginInvoke(
                                () =>
                                {
                                    if (count > limit)
                                    {
                                        // MessageBox.Show(string.Format("number of available artists ({0}) exceeds supported limit ({1})", count, limit));
                                    }
                                    tcs.SetResult(tracks);
                                });
                        }
                    },
                    webRequest);




            }, request);



            //var getRequestStreamTask = Task.Factory.FromAsync(
            //    requestStreamAr,
            //    ar =>
            //        ,
            //    TaskCreationOptions.None,
            //    TaskScheduler.FromCurrentSynchronizationContext());

            return tcs.Task;
        }

        public Task<IEnumerable<SynoItem>> GetAlbumsForArtistAsync(SynoItem artist)
        {
            var tcs = new TaskCompletionSource<IEnumerable<SynoItem>>();

            string urlBase = string.Format("http://{0}:{1}", this.Host, this.Port);
            // var url = urlBase + "/webman/modules/AudioStation/webUI/audio_browse.cgi";
            var url = urlBase + this.versionDependentResourcesProvider.GetAudioSearchWebserviceRelativePath(this.DsmVersion); 

            HttpWebRequest request = BuildRequest(url);

            int limit = 100;
            string postString = string.Format(@"action=browse&target={0}&server=musiclib_music_aa&category=&keyword={0}&start=0&sort=title&dir=ASC&limit={1}", HttpUtility.UrlEncode(artist.ItemID), limit);

            byte[] postBytes = System.Text.Encoding.UTF8.GetBytes(postString);

            var requestStreamAr = request.BeginGetRequestStream(ar =>
            {
                // Just make sure we retrieve the right web request : no access to modified closure.
                HttpWebRequest webRequest = (HttpWebRequest)ar.AsyncState;

                var requestStream = webRequest.EndGetRequestStream(ar);
                requestStream.Write(postBytes, 0, postBytes.Length);
                requestStream.Close();

                var getResponseAr = webRequest.BeginGetResponse(
                    responseAr =>
                    {
                        // Just make sure we retrieve the right web request : no access to modified closure.                        
                        var httpWebRequest = responseAr.AsyncState;

                        var webResponse = webRequest.EndGetResponse(responseAr);
                        var responseStream = webResponse.GetResponseStream();
                        var reader = new StreamReader(responseStream);
                        var content = reader.ReadToEnd();

                        long count;
                        IEnumerable<SynoItem> albums;
                        SynologyJsonDeserializationHelper.ParseSynologyAlbums(content, out albums, out count, urlBase);

                        var isOnUiThread = Deployment.Current.Dispatcher.CheckAccess();
                        if (isOnUiThread)
                        {
                            if (count > limit)
                            {
                                // MessageBox.Show(string.Format("number of available artists ({0}) exceeds supported limit ({1})", count, limit));
                            }

                            tcs.SetResult(albums);

                            // callback(tracks);
                        }
                        else
                        {
                            Deployment.Current.Dispatcher.BeginInvoke(
                                () =>
                                {
                                    if (count > limit)
                                    {
                                        // MessageBox.Show(string.Format("number of available artists ({0}) exceeds supported limit ({1})", count, limit));
                                    }
                                    tcs.SetResult(albums);
                                });
                        }
                    },
                    webRequest);
            }, request);



            //var getRequestStreamTask = Task.Factory.FromAsync(
            //    requestStreamAr,
            //    ar =>
            //        ,
            //    TaskCreationOptions.None,
            //    TaskScheduler.FromCurrentSynchronizationContext());

            return tcs.Task;
        }

        public Task<IEnumerable<SynoTrack>> GetTracksForAlbumAsync(SynoItem album)
        {
            var tcs = new TaskCompletionSource<IEnumerable<SynoTrack>>(album);

            string urlBase = string.Format("http://{0}:{1}", this.Host, this.Port);
            // var url = urlBase + "/webman/modules/AudioStation/webUI/audio_browse.cgi";
            var url = urlBase + this.versionDependentResourcesProvider.GetAudioSearchWebserviceRelativePath(this.DsmVersion); 

            HttpWebRequest request = BuildRequest(url);

            int limit = 100;
            string postString = string.Format(@"action=browse&target={0}&server=musiclib_music_aa&category=&keyword=&start=0&sort=title&dir=ASC&limit={1}", HttpUtility.UrlEncode(album.ItemID), limit);


            byte[] postBytes = System.Text.Encoding.UTF8.GetBytes(postString);

            var requestStreamAr = request.BeginGetRequestStream(ar =>
            {
                // Just make sure we retrieve the right web request : no access to modified closure.
                HttpWebRequest webRequest = (HttpWebRequest)ar.AsyncState;

                var requestStream = webRequest.EndGetRequestStream(ar);
                requestStream.Write(postBytes, 0, postBytes.Length);
                requestStream.Close();

                var getResponseAr = webRequest.BeginGetResponse(
                    responseAr =>
                    {
                        // Just make sure we retrieve the right web request : no access to modified closure.                        
                        var httpWebRequest = responseAr.AsyncState;

                        var webResponse = webRequest.EndGetResponse(responseAr);
                        var responseStream = webResponse.GetResponseStream();
                        var reader = new StreamReader(responseStream);
                        var content = reader.ReadToEnd();

                        long count;
                        IEnumerable<SynoTrack> tracks;
                        SynologyJsonDeserializationHelper.ParseSynologyTracks(content, out tracks, out count, urlBase);
                        
                        tracks = tracks.OrderBy(o => o.Track);

                        var isOnUiThread = Deployment.Current.Dispatcher.CheckAccess();
                        if (isOnUiThread)
                        {
                            if (count > limit)
                            {
                                // MessageBox.Show(string.Format("number of available artists ({0}) exceeds supported limit ({1})", count, limit));
                            }

                            tcs.SetResult(tracks);

                            // callback(tracks);
                        }
                        else
                        {
                            Deployment.Current.Dispatcher.BeginInvoke(
                                () =>
                                {
                                    if (count > limit)
                                    {
                                        // MessageBox.Show(string.Format("number of available artists ({0}) exceeds supported limit ({1})", count, limit));
                                    }
                                    tcs.SetResult(tracks);
                                });
                        }
                    },
                    webRequest);




            }, request);



            //var getRequestStreamTask = Task.Factory.FromAsync(
            //    requestStreamAr,
            //    ar =>
            //        ,
            //    TaskCreationOptions.None,
            //    TaskScheduler.FromCurrentSynchronizationContext());

            return tcs.Task;
        }

        public Task<IEnumerable<SynoItem>> SearchAlbums(string album)
        {      
      
            TaskCompletionSource<IEnumerable<SynoItem>> tcs = new TaskCompletionSource<IEnumerable<SynoItem>>();
            
            string urlBase = string.Format("http://{0}:{1}", this.Host, this.Port);
            // var url = urlBase + "/webman/modules/AudioStation/webUI/audio_browse.cgi";
            var url = urlBase + this.versionDependentResourcesProvider.GetAudioSearchWebserviceRelativePath(this.DsmVersion); 

            HttpWebRequest request = BuildRequest(url);

            int limit = 100;
            string postString = string.Format(@"action=search&target=musiclib_music_aa&server=musiclib_root&keyword={0}&start=0&limit={1}", album, limit);
            byte[] postBytes = System.Text.Encoding.UTF8.GetBytes(postString);

            var requestStreamAr = request.BeginGetRequestStream(ar =>
            {
                        // Just make sure we retrieve the right web request : no access to modified closure.
                        HttpWebRequest webRequest = (HttpWebRequest)ar.AsyncState;

                        var requestStream = webRequest.EndGetRequestStream(ar);
                        requestStream.Write(postBytes, 0, postBytes.Length);
                        requestStream.Close();

                        var getResponseAr = webRequest.BeginGetResponse(
                            responseAr =>
                                {
                                   // Just make sure we retrieve the right web request : no access to modified closure.                        
                                    var httpWebRequest = responseAr.AsyncState;

                                    var webResponse = webRequest.EndGetResponse(responseAr);
                                    var responseStream = webResponse.GetResponseStream();
                                    var reader = new StreamReader(responseStream);
                                    var content = reader.ReadToEnd();

                                    long count;
                                    IEnumerable<SynoItem> tracks;
                                    SynologyJsonDeserializationHelper.ParseSynologyAlbums(
                                        content, out tracks, out count, urlBase);

                                    var isOnUiThread = Deployment.Current.Dispatcher.CheckAccess();
                                    if (isOnUiThread)
                                    {
                                        if (count > limit)
                                        {
                                            // MessageBox.Show(string.Format("number of available artists ({0}) exceeds supported limit ({1})", count, limit));
                                        }

                                        tcs.SetResult(tracks);

                                        // callback(tracks);
                                    }
                                    else
                                    {
                                        Deployment.Current.Dispatcher.BeginInvoke(
                                            () =>
                                                {
                                                    if (count > limit)
                                                    {
                                                        // MessageBox.Show(string.Format("number of available artists ({0}) exceeds supported limit ({1})", count, limit));
                                                    }
                                                    tcs.SetResult(tracks);
                                                });
                                    }
                            }, 
                            webRequest);

                      


            }, request);



            //var getRequestStreamTask = Task.Factory.FromAsync(
            //    requestStreamAr,
            //    ar =>
            //        ,
            //    TaskCreationOptions.None,
            //    TaskScheduler.FromCurrentSynchronizationContext());

            return tcs.Task;
        }

        /// <summary>
        /// Gets the remote file network stream.
        /// </summary>
        /// <param name="synoTrack">The track wor which to retrieve the stream.</param>
        /// <param name="callback">The method to call after the stream is open. The HttpResponse is passed as argument</param>
        /// <remarks>The caller is responsible for closing the stream after the call to DownloadFile returns</remarks>
        public void GetFileStream(SynoTrack synoTrack, Action<WebResponse, SynoTrack> callback)
        {
            if (synoTrack == null)
            {
                throw new ArgumentNullException("synoTrack");
            }

            if (callback == null)
            {
                throw new ArgumentNullException("callback");
            }

            var client = new WebClient();

            var url = GetUrlForTrack(synoTrack);
            var request = (HttpWebRequest)WebRequest.Create(url);

            request.CookieContainer = new CookieContainer();
            request.CookieContainer.SetCookies(new Uri(url), this.Token);
            //Set request headers.
            request.Accept = "application/x-ms-application, image/jpeg, application/xaml+xml, image/gif, image/pjpeg, application/x-ms-xbap, application/vnd.ms-excel, application/vnd.ms-powerpoint, application/msword, */*";
            //request.Headers.Set(HttpRequestHeader.AcceptLanguage, "en-US");
            //request.UserAgent = "Mozilla/4.0 (compatible; MSIE 8.0; Windows NT 6.1; WOW64; Trident/4.0; SLCC2; .NET CLR 2.0.50727; .NET CLR 3.5.30729; .NET CLR 3.0.30729; Media Center PC 6.0; .NET4.0C; .NET4.0E; InfoPath.2; OfficeLiveConnector.1.5; OfficeLivePatch.1.3; Zune 4.7)";
            //request.Headers.Set(HttpRequestHeader.AcceptEncoding, "gzip, deflate");
            //request.Headers.Set(HttpRequestHeader.Cookie, @"__utma=11735858.713408819.1284879944.1294622128.1297023459.8; __utmz=11735858.1297023459.8.8.utmcsr=google|utmccn=(organic)|utmcmd=organic|utmctr=TiltEffect%20Toolkit; __qca=P0-1026483267-1284879945945");

            request.AllowReadStreamBuffering = false;
            request.BeginGetResponse(OnFileDownloadResponseReceived, new FileDownloadResponseReceivedUserState(request, callback, synoTrack));

        }

        public string GetUrlForTrack(SynoTrack synoTrack)
        {
            // hack : Synology's webserver doesn't accept the + character as a space : it needs a %20, and it needs to have special characters such as '&' to be encoded with %20 as well, so an HtmlEncode is not an option, since even if a space would be encoded properly, an ampersand (&) would be translated into &amp;
            var relativePathToAudioStreamService =
                this.versionDependentResourcesProvider.GetAudioStreamWebserviceRelativePath(this.DsmVersion);
            string url = string.Format("http://{0}:{1}{2}/0.mp3?action=streaming&songpath={3}", this.Host, this.Port,
                                       relativePathToAudioStreamService,
                                       HttpUtility.UrlEncode(synoTrack.Res).Replace("+", "%20").Replace("&", "%26"));
            return url;
        }

        private void OnFileDownloadResponseReceived(IAsyncResult ar)
        {            
            var userState = (FileDownloadResponseReceivedUserState)ar.AsyncState;
            WebResponse response;
            try
            {
                response = userState.Request.EndGetResponse(ar);
            }
            catch (ArgumentNullException argumentNullException)
            {
                // TODO : Call the Error callback;
                throw;
            }

            if (response.ContentLength < 0)
            {
                Debugger.Break();
            }

            userState.GetResponseCallback(response, userState.SynoTrack);
        }

        public void LoginAsync(string login, string password, Action<string> callback, Action<Exception> callbackError, bool useSsl)
        {
            if (login == null) throw new ArgumentNullException("login");
            if (password == null) throw new ArgumentNullException("password");

            WebClient client = new WebClient();

            Uri uri = new UriBuilder
                {
                    Host = this.Host,
                    Path = @"/webman/login.cgi",
                    Query = string.Format("username={0}&passwd={1}", login, password),
                    Port = this.Port,
                    Scheme = useSsl ? "https" : "http"
                }.Uri;

            client.DownloadStringCompleted += (sender, e) =>
                                                  {
                                                      if (e.Error != null)
                                                      {
                                                          if (uri.Scheme == "https")
                                                          {
                                                              throw new SynoNetworkException("Open Syno could not connect to the server. Please make sure your server's SSL certificate has been issued by a trusted Certificate Authority. see http://bit.ly/qODji5  for further detail.", e.Error);
                                                          }

                                                          throw new SynoNetworkException("Open Syno could not complete the operation. Please check that your phone is not in flight mode and that you are getting a proper signal.", e.Error);
                                                      }
                                                      else
                                                      {
                                                          string rawCookie = ((WebClient)sender).ResponseHeaders["Set-Cookie"];
                                                          if (rawCookie == null)
                                                          {
                                                              try
                                                              {
                                                                  if (JObject.Parse(e.Result)["success"].Value<bool>() != true)
                                                                  {
                                                                      throw new SynoLoginException("The login and the password don't match, please check your credentials", null);
                                                                  }
                                                              }
                                                              catch (JsonReaderException exception)
                                                              {
                                                                  PiggybackingJsonReaderException extendedException = new PiggybackingJsonReaderException("Failed JSON document was : " + e.Result, exception);
                                                                  callbackError(extendedException);
                                                              }
                                                              
                                                          }
                                                          else
                                                          {
                                                              string cookie = rawCookie.Split(';').Where(s => s.StartsWith("id=")).Single();
                                                              this.Token = cookie;
                                                              
                                                              // Delete ascii urls patches
                                                              
                                                          }                                                          
                                                          
                                                          callback(this.Token);
                                                      }

                                                  };

            client.DownloadStringAsync(uri);
        }


        public void SearchAllMusic(string pattern, Action<IEnumerable<SynoTrack>> callback, Action<Exception> callbackError)
        {
            string urlBase = string.Format("http://{0}:{1}", this.Host, this.Port);
            // var url = urlBase + "/webman/modules/AudioStation/webUI/audio_browse.cgi";
            var url = urlBase + this.versionDependentResourcesProvider.GetAudioSearchWebserviceRelativePath(this.DsmVersion); 

            HttpWebRequest request = BuildRequest(url);

            int limit = 100;
            string postString = string.Format(@"action=search&target=musiclib_root&server=musiclib_root&category=all&keyword={0}&start=0&limit={1}", pattern, limit);
            byte[] postBytes = System.Text.Encoding.UTF8.GetBytes(postString);


            request.BeginGetRequestStream(ar =>
            {
                // Just make sure we retrieve the right web request : no access to modified closure.
                HttpWebRequest webRequest = (HttpWebRequest)ar.AsyncState;

                var requestStream = webRequest.EndGetRequestStream(ar);
                requestStream.Write(postBytes, 0, postBytes.Length);
                requestStream.Close();

                request.BeginGetResponse(
                    responseAr =>
                    {
                        // Just make sure we retrieve the right web request : no access to modified closure.                        
                        var httpWebRequest = responseAr.AsyncState;

                        var webResponse = webRequest.EndGetResponse(responseAr);
                        var responseStream = webResponse.GetResponseStream();
                        var reader = new StreamReader(responseStream);
                        var content = reader.ReadToEnd();

                        long count;
                        IEnumerable<SynoTrack> tracks;
                        SynologyJsonDeserializationHelper.ParseSynologyTracks(content, out tracks, out count, urlBase);

                        var isOnUiThread = Deployment.Current.Dispatcher.CheckAccess();
                        if (isOnUiThread)
                        {
                            if (count > limit)
                            {
                                // MessageBox.Show(string.Format("number of available artists ({0}) exceeds supported limit ({1})", count, limit));
                            }
                            callback(tracks);
                        }
                        else
                        {
                            Deployment.Current.Dispatcher.BeginInvoke(() =>
                            {
                                if (count > limit)
                                {
                                    // MessageBox.Show(string.Format("number of available artists ({0}) exceeds supported limit ({1})", count, limit));
                                }
                                callback(tracks);
                            });
                        }
                    },
                    webRequest);
            },
                request);
        }

        public void SearchArtist(string pattern, Action<IEnumerable<SynoItem>> callback, Action<Exception> callbackError)
        {
            string urlBase = string.Format("http://{0}:{1}", this.Host, this.Port);
            var url = urlBase + this.versionDependentResourcesProvider.GetAudioSearchWebserviceRelativePath(this.DsmVersion); 
            HttpWebRequest request;

            try
            {
                request = BuildRequest(url);
            }
            catch (UriFormatException e)
            {                
                throw new NotSupportedException("given url format is not supported : " + url,  e);
            }

            // TODO : Find a way to retrieve the whole list by chunks of smaller size to have something to show earlier... or stream the JSON and parse it on the fly if it is possible
            int limit = 5000;
            string postString = string.Format(@"sort=title&dir=ASC&action=browse&target=musiclib_music_aa&server=musiclib_music_aa&category=&keyword={0}&start=0&limit={1}&library=shared", pattern, limit);
            byte[] postBytes = System.Text.Encoding.UTF8.GetBytes(postString);

            request.BeginGetRequestStream(ar =>
                {
                    // Just make sure we retrieve the right web request : no access to modified closure.
                    HttpWebRequest webRequest = (HttpWebRequest)ar.AsyncState;

                    var requestStream = webRequest.EndGetRequestStream(ar);
                    requestStream.Write(postBytes, 0, postBytes.Length);
                    requestStream.Close();

                    request.BeginGetResponse(
                        responseAr =>
                        {
                            // Just make sure we retrieve the right web request : no access to modified closure.                        
                            var httpWebRequest = responseAr.AsyncState;
                            if (!webRequest.HaveResponse)
                            {
                                throw new SynoSearchException("Error connecting to search engine", null);
                                // FIXME : Use an error handling service
                                //var action = new Action(() =>MessageBox.Show("Error connecting to search engine", "Connection error",MessageBoxButton.OK));   

                                //if (Deployment.Current.CheckAccess())
                                //{
                                //    action();
                                //}
                                //else
                                //{
                                //    Deployment.Current.Dispatcher.BeginInvoke(action);
                                //}
                                //return;
                            }
                            var webResponse = webRequest.EndGetResponse(responseAr);
                            var responseStream = webResponse.GetResponseStream();
                            var reader = new StreamReader(responseStream);
                            var content = reader.ReadToEnd();

                            long count;
                            IEnumerable<SynoItem> artists;
                            
                            SynologyJsonDeserializationHelper.ParseSynologyArtists(content, out artists, out count, urlBase);



                            var isOnUiThread = Deployment.Current.Dispatcher.CheckAccess();
                            if (isOnUiThread)
                            {
                                if (count > limit)
                                {
                                    // FIXME : Use an error handling service
                                    // MessageBox.Show(string.Format("number of available artists ({0}) exceeds supported limit ({1})", count, limit));
                                }
                                callback(artists);
                            }
                            else
                            {
                                Deployment.Current.Dispatcher.BeginInvoke(
                                    new Action<IEnumerable<SynoItem>>(a =>
                                    {
                                        if (count > limit)
                                        {
                                            // FIXME : Use an error handling service
                                            // MessageBox.Show(string.Format("number of available artists ({0}) exceeds supported limit ({1})", count, limit));
                                        }
                                        callback(a);
                                    }),
                                    new[] { artists });
                            }
                        },
                        webRequest);
                },
                request);

            //CookieAwareWebClient wc = new CookieAwareWebClient();

            //Uri uri =
            //    new UriBuilder
            //    {
            //        Host = _host,
            //        Path = @"/webman/login.cgi",
            //        Query = postString,
            //        Port = _port
            //    }.Uri;

            //HttpWebRequest webRequest = (HttpWebRequest)wc.GetWebRequest(uri);

            //webRequest.CookieContainer = new CookieContainer();
            //webRequest.CookieContainer.SetCookies(new Uri(url), _token);

            //wc.DownloadStringCompleted += (sender, ea) =>
            //                                  {
            //                                      if (ea.Error != null)
            //                                      {
            //                                          callbackError(ea.Error);
            //                                          return;
            //                                      }
            //                                      long count;
            //                                      IEnumerable<SynoItem> artists;
            //                                      SynologyJsonDeserializationHelper.ParseSynologyArtists(
            //                                                                        ea.Result,
            //                                                                        out artists,
            //                                                                        out count,
            //                                                                        urlBase);
            //                                      callback(artists);
            //                                  };
            //wc.DownloadStringAsync(uri);          
        }

        
        private HttpWebRequest BuildRequest(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);

            request.Accept = "*/*";
            request.ContentType = "application/x-www-form-urlencoded; charset=UTF-8";

            // Not supported yet, but that would decrease the bandwidth usage from 1.3 Mb to 83 Kb ... Pretty dramatic, ain't it ?
            //request.Headers["Accept-Encoding"] = "gzip, deflate";

            request.UserAgent = "OpenSyno";
            request.CookieContainer = new CookieContainer();
            request.CookieContainer.SetCookies(new Uri(url), this.Token);

            request.Method = "POST";
            return request;
        }

        public void GetAlbumsForArtist(SynoItem artist, Action<IEnumerable<SynoItem>, long, SynoItem> callback, Action<Exception> callbackError)
        {
            string urlBase = string.Format("http://{0}:{1}", this.Host, this.Port);
            var url = urlBase + this.versionDependentResourcesProvider.GetAudioSearchWebserviceRelativePath(this.DsmVersion);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);

            request.Accept = "*/*";
            request.ContentType = "application/x-www-form-urlencoded; charset=UTF-8";

            // Not supported yet, but that would decrease the bandwidth usage from 1.3 Mb to 83 Kb ... Pretty dramatic, ain't it ?
            //request.Headers["Accept-Encoding"] = "gzip, deflate";

            request.UserAgent = "OpenSyno";
            request.CookieContainer = new CookieContainer();
            request.CookieContainer.SetCookies(new Uri(url), this.Token);

            request.Method = "POST";

            int limit = 10000;
            // string postString = string.Format(@"action=browse&target={0}&server=musiclib_music_aa&category=&keyword=&start=0&sort=title&dir=ASC&limit={1}", HttpUtility.UrlEncode(artist.ItemID), limit);

            string postString = string.Format(@"sort=title&dir=ASC&action=browse&target={0}&server=musiclib_music_aa&category=&keyword=&start=0&limit={1}&library=shared&category_name={2}&artistType=artist", HttpUtility.UrlEncode(artist.ItemID), limit, HttpUtility.UrlEncode(artist.Title));


            byte[] postBytes = System.Text.Encoding.UTF8.GetBytes(postString);

            request.BeginGetRequestStream(ar =>
            {
                // Just make sure we retrieve the right web request : no access to modified closure.
                HttpWebRequest webRequest = (HttpWebRequest)ar.AsyncState;

                var requestStream = webRequest.EndGetRequestStream(ar);
                requestStream.Write(postBytes, 0, postBytes.Length);
                requestStream.Close();

                request.BeginGetResponse(
                    responseAr =>
                    {
                        // Just make sure we retrieve the right web request : no access to modified closure.                        
                        var httpWebRequest = responseAr.AsyncState;

                        var webResponse = webRequest.EndGetResponse(responseAr);
                        var responseStream = webResponse.GetResponseStream();
                        var reader = new StreamReader(responseStream);
                        var content = reader.ReadToEnd();

                        long count;
                        IEnumerable<SynoItem> albums;
                        SynologyJsonDeserializationHelper.ParseSynologyAlbums(content, out albums, out count, urlBase);



                        var isOnUiThread = Deployment.Current.Dispatcher.CheckAccess();
                        if (isOnUiThread)
                        {
                            if (count > limit)
                            {
                                // FIXME : Use an error handling service
                                // MessageBox.Show(string.Format("number of available albums ({0}) exceeds supported limit ({1})", count, limit));
                            }
                            callback(albums, count, artist);
                        }
                        else
                        {
                            Deployment.Current.Dispatcher.BeginInvoke(() =>
                            {
                                if (count > limit)
                                {
                                    // FIXME : Use an error handling service
                                    // MessageBox.Show(string.Format("number of available artists ({0}) exceeds supported limit ({1})", count, limit));
                                }
                                callback(albums, count, artist);
                            });
                        }
                    },
                    webRequest);
            },
                request);
        }

        public void GetTracksForAlbum(SynoItem album,SynoItem artist, Action<IEnumerable<SynoTrack>, long, SynoItem> callback, Action<Exception> callbackError)
        {
            string urlBase = string.Format("http://{0}:{1}", this.Host, this.Port);

            var url = urlBase + this.versionDependentResourcesProvider.GetAudioSearchWebserviceRelativePath(this.DsmVersion);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);

            request.Accept = "*/*";
            request.ContentType = "application/x-www-form-urlencoded; charset=UTF-8";

            // Not supported yet, but that would decrease the bandwidth usage from 1.3 Mb to 83 Kb ... Pretty dramatic, ain't it ?
            //request.Headers["Accept-Encoding"] = "gzip, deflate";

            request.UserAgent = "OpenSyno";
            request.CookieContainer = new CookieContainer();
            request.CookieContainer.SetCookies(new Uri(url), this.Token);

            request.Method = "POST";

            int limit = 10000;
            // prior to 4.1  : "action=browse&target={0}&server=musiclib_music_aa&category=&keyword=&start=0&sort=title&dir=ASC&limit={1}"
            string postString = string.Format(@"sort=album&dir=ASC&action=browse&target={0}&server=musiclib_root&category=&keyword=&start=0&limit={1}&library=shared&category_name={2}&artistType=artist&album_name={3}", HttpUtility.UrlEncode(album.ItemID), limit, artist.Title, album.Title);
            byte[] postBytes = System.Text.Encoding.UTF8.GetBytes(postString);

            request.BeginGetRequestStream(ar =>
            {
                // Just make sure we retrieve the right web request : no access to modified closure.
                HttpWebRequest webRequest = (HttpWebRequest)ar.AsyncState;

                var requestStream = webRequest.EndGetRequestStream(ar);
                requestStream.Write(postBytes, 0, postBytes.Length);
                requestStream.Close();

                request.BeginGetResponse(
                    responseAr =>
                    {
                        // Just make sure we retrieve the right web request : no access to modified closure.                        
                        var httpWebRequest = responseAr.AsyncState;                            
                        WebResponse webResponse;
                        try
                        {
                            webResponse = webRequest.EndGetResponse(responseAr);
                        }
                        catch (WebException exception)
                        {
                            throw new SynoNetworkException("The remote server did not respond to our request. Please, check that the sinal is strong enough and try again.", exception);                            
                        }
                        var responseStream = webResponse.GetResponseStream();
                        var reader = new StreamReader(responseStream);
                        var content = reader.ReadToEnd();

                        long total;
                        IEnumerable<SynoTrack> tracks;
                        SynologyJsonDeserializationHelper.ParseSynologyTracks(content, out tracks, out total, urlBase);

                        tracks = tracks.OrderBy(o => o.Track);

                        var isOnUiThread = Deployment.Current.Dispatcher.CheckAccess();
                        if (isOnUiThread)
                        {
                            if (total > limit)
                            {
                                // FIXME : Use an error handling service
                                // MessageBox.Show(string.Format("number of available albums ({0}) exceeds supported limit ({1})", total, limit));
                            }
                            callback(tracks, total, album);
                        }
                        else
                        {
                            Deployment.Current.Dispatcher.BeginInvoke(() =>
                            {
                                if (total > limit)
                                {
                                    // FIXME : Use an error handling service
                                    // MessageBox.Show(string.Format("number of available artists ({0}) exceeds supported limit ({1})", total, limit));
                                }
                                callback(tracks, total, album);
                            });
                        }
                    },
                    webRequest);
            },
                request);
        }

        public bool IsSignedIn
        {
            get
            {
                return this.Token != null;
            }
        }
    }

    public class SynoSearchException : Exception
    {
        public SynoSearchException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    public class SynoLoginException : Exception
    {
        public SynoLoginException(string message, Exception innerException) : base(message, innerException)
        {            
        }
    }

    public class SynoNetworkException : Exception
    {
        public SynoNetworkException(string message, Exception innerException) : base(message, innerException)
        {            
        }
    }
}