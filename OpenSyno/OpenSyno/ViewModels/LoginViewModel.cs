﻿namespace OpenSyno
{
    using System;
    using System.IO.IsolatedStorage;
    using System.Windows;
    using System.Windows.Input;

    using Microsoft.Practices.Prism.Commands;
    using Microsoft.Practices.Prism.Events;

    using OpenSyno.Services;
    using OpenSyno.ViewModels;

    using Synology.AudioStationApi;

    public class LoginViewModel : ViewModelBase
    {
        private readonly IEventAggregator _eventAggregator;
        private IAudioStationSession _audioStationSession;
        private readonly IOpenSynoSettings _synoSettings;

        private IPageSwitchingService _pageSwitchingService;

        public LoginViewModel(IPageSwitchingService pageSwitchingService, IEventAggregator eventAggregator, IAudioStationSession audioStationSession, IOpenSynoSettings synoSettings)
        {
            if (pageSwitchingService == null) throw new ArgumentNullException("pageSwitchingService");
            if (eventAggregator == null) throw new ArgumentNullException("eventAggregator");
            if (audioStationSession == null) throw new ArgumentNullException("audioStationSession");
            SignInCommand = new DelegateCommand(OnSignIn);
            _pageSwitchingService = pageSwitchingService;
            _eventAggregator = eventAggregator;
            _audioStationSession = audioStationSession;
            _synoSettings = synoSettings;
            UserName = _synoSettings.UserName;
            UseSsl = _synoSettings.UseSsl;
            Password = _synoSettings.Password;
            Host = _synoSettings.Host;
            Port = _synoSettings.Port;
        }

        public string UserName { get; set; }

        public string Password { get; set; }

        public string Host { get; set; }
        
        public int Port { get; set; }

        private void OnSignIn()
        {
            // Store it in SynoSettings and serialize SynoSettings in the Isolated Storage.
            _synoSettings.UserName = UserName;
            _synoSettings.Password = Password;
            _synoSettings.UseSsl = UseSsl;
            _synoSettings.Host = Host;
            _synoSettings.Port = Port;
            _audioStationSession.Host = Host;
            _audioStationSession.Port = Port;
            try
            {
                _audioStationSession.LoginAsync(this.UserName, this.Password, this.OnLoginAsyncCompleted, this.OnLoginAsyncException, this._synoSettings.UseSsl);
            }
            catch (ArgumentNullException exception)
            {
                // FIXME : Use noification service instead
                MessageBox.Show(
                    "The connection settings don't look valid, please make sure they are entered correctly.",
                    "Credentials not valid",
                    MessageBoxButton.OK);
            }
            catch (UriFormatException exception)
            {
                // FIXME : Use noification service instead
                MessageBox.Show(
                    "The format of the provided hostname is not in valid. Check that it is not prefixedit with http:// or https://",
                    "The host name is not valid",
                    MessageBoxButton.OK);
            }
        }

        public bool UseSsl { get; set; }

        private void OnLoginAsyncException(Exception exception)
        {
            throw exception;
        }

        private void OnLoginAsyncCompleted(string token)
        {
            _synoSettings.Token = token;

            // if it worked : let's save the credentials.
            IsolatedStorageSettings.ApplicationSettings["SynoSettings"] = _synoSettings;
            IsolatedStorageSettings.ApplicationSettings.Save();

            _pageSwitchingService.NavigateToPreviousPage();
        }

        public ICommand SignInCommand { get; set; }
    }
}