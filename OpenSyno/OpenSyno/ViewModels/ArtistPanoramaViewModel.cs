﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Microsoft.Practices.Prism.Events;
using Synology.AudioStationApi;

namespace OpenSyno.ViewModels
{
    using System.Windows.Navigation;

    using OpenSyno.Services;

    public class ArtistPanoramaViewModel : ViewModelBase
    {
        private readonly ISearchService _searchService;
        private readonly IEventAggregator _eventAggregator;

        private readonly PageSwitchingService _pageSwitchingService;

        public ObservableCollection<ArtistPanoramaItem> ArtistItems { get; set; }

        private string _artistName;
        private const string ArtistNamePropertyName = "ArtistName";
        private const string IsBusyPropertyName = "IsBusy";

        public ArtistPanoramaItemKind PanoramaItemKind { get; set; }

        public ArtistPanoramaViewModel(ISearchService searchService, IEventAggregator eventAggregator, PageSwitchingService pageSwitchingService, SynoItem artist)
        {
            if (searchService == null)
            {
                throw new ArgumentNullException("searchService");
            }

            if (eventAggregator == null)
            {
                throw new ArgumentNullException("eventAggregator");
            }

            if (pageSwitchingService == null)
            {
                throw new ArgumentNullException("pageSwitchingService");
            }

            if (artist == null)
            {
                throw new ArgumentNullException("artist");
            }

            _searchService = searchService;
            _eventAggregator = eventAggregator;
            _pageSwitchingService = pageSwitchingService;
            ArtistItems = new ObservableCollection<ArtistPanoramaItem>();
            ArtistItems.CollectionChanged += StartMonitoringElements;
            foreach (ArtistPanoramaItem artistItem in ArtistItems)
            {
                artistItem.PropertyChanged += UpdateBusyness;
            }


            eventAggregator.GetEvent<CompositePresentationEvent<SelectedArtistChangedAggregatedEvent>>().Subscribe(OnSelectedArtistChanged, true);
            LoadArtistInfo(artist);

        }

        private void UpdateBusyness(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == ArtistPanoramaItem.IsBusyPropertyName)
            {
                IsBusy = ArtistItems.Any(o => o.IsBusy);
            }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get { return _isBusy; }
            set
            {
                _isBusy = value;
                OnPropertyChanged(IsBusyPropertyName);
            }
        }

        private void StartMonitoringElements(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (ArtistPanoramaItem artistItem in e.NewItems)
                {
                    artistItem.PropertyChanged += UpdateBusyness;
                } 
            }

            if (e.OldItems != null)
            {
                foreach (ArtistPanoramaItem artistItem in e.OldItems)
                {
                    artistItem.PropertyChanged -= UpdateBusyness;
                }
            }
        }

        private void OnSelectedArtistChanged(SelectedArtistChangedAggregatedEvent ea)
        {
            LoadArtistInfo(ea.Artist);
        }

        private void LoadArtistInfo(SynoItem artist)
        {
            ArtistName = artist.Title;
            GetAlbumsForArtist(artist);
        }

        public string ArtistName
        {
            get
            {
                return _artistName;
            }
            set 
            {
                _artistName = value;
                OnPropertyChanged(ArtistNamePropertyName);
            }
        }

        private void GetAlbumsForArtist(SynoItem artist)
        {
            _searchService.GetAlbumsForArtist(artist, GetAlbumsForArtistCompleted);
        }

        private void GetAlbumsForArtistCompleted(IEnumerable<SynoItem> albums, long total, SynoItem artist)
        {
            // make sure the old items are cleared.
            ArtistItems.Clear();

            // add the page for the list of albums.
            var albumsListPanel = new ArtistPanoramaAlbumsListItem(albums, artist, _pageSwitchingService);

            ArtistItems.Add(albumsListPanel);

            // the "all albums" items
            var allmusic = albums.Where(o => o.ItemID.StartsWith("musiclib_music_artist"));

            foreach (var album in albums.Except(allmusic))
            {
                var albumDetail = new ArtistPanoramaAlbumDetailItem(album, _searchService, _eventAggregator);                
                ArtistItems.Add(albumDetail);
            }

            foreach (var album in allmusic)
            {
                var albumDetail = new ArtistPanoramaAlbumDetailItem(album, _searchService, _eventAggregator);
                ArtistItems.Add(albumDetail);
            }
            
        }
    }
}