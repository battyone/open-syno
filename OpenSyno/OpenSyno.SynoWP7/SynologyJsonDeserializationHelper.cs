﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Newtonsoft.Json.Linq;
using Synology.AudioStationApi;

namespace OpenSyno.SynoWP7
{
    public class SynologyJsonDeserializationHelper
    {
        public static void ParseSynologyArtists(string result, out IEnumerable<SynoItem> artists, out long total, string urlBase)
        {
            JObject jObject = JObject.Parse(result);
            total = 0L;
            artists = null;
            artists = from album in jObject["items"]
                      select new SynoItem
                      {
                          AlbumArtUrl = BuildAbsoluteAlbumArtUrl(urlBase, album["albumArtURL"].Value<string>()),
                          Icon = album["icon"].Value<string>(),
                          IsContainer = album["is_container"].Value<bool>(),
                          IsTrack = album["is_track"].Value<bool>(),
                          ItemID = album["item_id"].Value<string>(),
                          ItemPid = album["item_pid"].Value<string>(),
                          Sequence = album["sequence"].Value<int>(),
                          Support = album["support"].Value<bool>(),
                          Title = album["title"].Value<string>()
                      };
            total = jObject["total"].Value<long>();
        }

        private static string BuildAbsoluteAlbumArtUrl(string urlBase, string relativeAlbumArtUrl)
        {
            // in DSM 3.0
            // http://host/audio/[albumArtURL]

            // in DSM 3.1
            // http://host/webman/[albumArtURL]

            string rootPath = "/webman/";
            return urlBase + rootPath + relativeAlbumArtUrl;
        }

        public static void ParseSynologyAlbums(string content, out IEnumerable<SynoItem> albums, out long total, string urlBase)
        {
            ParseSynologyArtists(content, out albums, out total, urlBase);

            // need to do this for Synology 3.0 : 
            // NOTE : albums is passed with ToArray : the reason is that somehow, editing the albums fields while still in a form of a JSON.NET enumerable, modifications don't apply.
            // I must admit I didn't quite understand if it was a normal behavior or a bug in JSON.NET, but I was in a hurry and needed to fix this quickly - shame on me ;)
            albums = WorkaroundAlbumArtBug(albums.ToArray());
        }

        /// <summary>
        /// Workarounds the album art bug.
        /// </summary>
        /// <param name="albums">The albums.</param>
        /// <remarks>
        /// There a bug in Audio Station 3.0 where the AlbumArtUrl field does not point to an album art, but rather to an artist art. fixing the url on the client will work just fine untill they fix it.
        /// </remarks>
        private static IEnumerable<SynoItem> WorkaroundAlbumArtBug(IEnumerable<SynoItem> albums)
        {
            foreach (var synoItem in albums)
            {
                synoItem.AlbumArtUrl = synoItem.AlbumArtUrl.Replace("webUI/getcover.cgi/artist", "webUI/getcover.cgi/album");
            }
            return albums;
        }

        public static void ParseSynologyTracks(string content, out IEnumerable<SynoTrack> tracks, out long total, string urlBase)
        {
            JObject o = JObject.Parse(content);
            JToken items = o["items"];
            total = o["total"].Value<long>();
            tracks = from p in items
                    select new SynoTrack
                    {
                        Album = p["album"].Value<string>(),
                        AlbumArtUrl = BuildAbsoluteAlbumArtUrl(urlBase, p["albumArtURL"].Value<string>()),
                        Artist = p["artist"].Value<string>(),
                        Bitrate = p["bitrate"].Value<long>(),
                        Channels = p["channels"].Value<int>(),
                        //Class = p["class"].Value<string>(),
                        Disc = p["disc"].Value<int>(),
                        Duration = TimeSpan.FromSeconds(p["duration"].Value<int>()),
                        //DateTime.ParseExact(p["duration"].Value<string>(), "m:ss", CultureInfo.InvariantCulture,DateTimeStyles.AllowLeadingWhite).TimeOfDay, // doesn't work if m > 59
                        Genre = p["genre"].Value<string>(),
                        Icon = p["icon"].Value<string>(),
                        IsContainer = p["is_container"].Value<bool>(),
                        IsTrack = p["is_track"].Value<bool>(),
                        ItemID = p["item_id"].Value<string>(),
                        ItemPid = p["item_pid"].Value<string>(),
                        //ProtocolInfo = p["protocolinfo"].Value<string>(),
                        Res = p["res"].Value<string>(),
                        Sample = p["sample"].Value<long>(),
                        Sequence = p["sequence"].Value<int>(),
                        Size = long.Parse(p["size"].Value<string>()),
                        Support = p["support"].Value<bool>(),
                        Title = p["title"].Value<string>(),
                        Track = p["track"].Value<int>(),
                        Year = p["year"].Value<int>()
                    }
                ;
        }
    }
}