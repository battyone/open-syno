﻿namespace OpenSyno
{
    using System;
    using System.Threading;
    using System.Windows;
    using System.Windows.Threading;

    public class NotificationService : INotificationService
    {
        public void Warning(string warningMessage, string warningTitle)
        {
            MessageBox.Show(warningMessage, warningTitle, MessageBoxButton.OK);
        }

        public void Error(string message, string messageTitle)
        {

            if (!Deployment.Current.Dispatcher.CheckAccess())
            {
                Deployment.Current.Dispatcher.BeginInvoke(() => MessageBox.Show(message, messageTitle, MessageBoxButton.OK));
            }
            MessageBox.Show(message, messageTitle, MessageBoxButton.OK);
        }

        public MessageBoxResult WarningQuery(string warningMessage, string warningTitle, MessageBoxButton userResponseOptions)
        {
            return MessageBox.Show(warningMessage, warningTitle, userResponseOptions);
        }
    }
}