namespace OpemSyno.Contracts
{
    using System;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Windows.Input;

    using Synology.AudioStationApi;

    public interface IAlbumViewModel : INotifyPropertyChanged
    {
        event EventHandler Selected;

        ObservableCollection<ITrackViewModel> Tracks { get; set; }

        SynoItem Album { get; set; }

        ICommand SelectedCommand { get; set; }

        bool IsBusy { get; set; }

        ICommand SelectAllOrNoneCommand { get; set; }
    }
}