﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using Timer = System.Timers.Timer;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GabaLiveView.Video;
using GabaLiveView.Video.FFmpegVideoCore;
using GabaLiveView.Video.FFmpegVideoCore.Decode;
using System.Windows;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;
using SkiaSharp.Views;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace GabaLiveView
{
    internal partial class MainWindowViewModel : ObservableObject, INotifyPropertyChanged
    {
        public bool isDrawing = false;

        [ObservableProperty]
        private VideoReceiveArgs receiveArgs = null;

        [ObservableProperty]
        private bool isButtonOpenEnabled = true;

        [ObservableProperty]
        private Visibility infoVisible = Visibility.Hidden;

        [ObservableProperty]
        private Visibility topBarVisible = Visibility.Visible;

        [ObservableProperty]
        private Visibility topButtonVisible = Visibility.Hidden;

        [ObservableProperty]
        private string videoResolution = "Unknown";

        [ObservableProperty]
        private string videoFramerate = "Unknown";

        [ObservableProperty]
        private string videoBitrate = "Unknown";

        [ObservableProperty]
        private string videoFormat = "Unknown";

        [ObservableProperty]
        private string videoRecording = "OFF";

        [ObservableProperty]
        private string logMessage = "";

        IVideoCore videoCore;
        Timer infoTimer = new Timer();
        DispatcherTimer refreshTimer;
        SKElement frontendCanvas;

        public MainWindowViewModel(ref SKElement canvas)
        {
            frontendCanvas = canvas;

            infoTimer.Interval = 500;
            infoTimer.Elapsed += updateVideoInfoTimer;

            // 2024.7.5 Blackcat: Use Timer For Refreshing The Video (Prevent Calling Dispatcher Too Much Cause Playing Slowly)
            refreshTimer = new DispatcherTimer();
            refreshTimer.Interval = TimeSpan.FromMilliseconds(16);
            refreshTimer.Tick += RefreshTimer_Tick;

        }

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            // if frame is new, draw it
            if (isDrawing)
            {
                isDrawing = false;

                frontendCanvas.InvalidateVisual();
            }
        }

        private void updateVideoInfoTimer(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (ReceiveArgs != null)
            {
                VideoResolution = ReceiveArgs.width + "x" + ReceiveArgs.height;
                VideoFramerate  = ReceiveArgs.framerate.ToString("0.00") + " Fps";
                VideoFormat     = ReceiveArgs.format;
                VideoBitrate    = ReceiveArgs.bitrate.ToString("0.00") + " Kbps";
            }
        }

        [RelayCommand]
        public void ButtonOpen()
        {
            IsButtonOpenEnabled = false;

            string protocol = (StreamProtocol == 0) ? "rtsp" :
                              (StreamProtocol == 1) ? "rtmp" : "hls";

            if (videoCore == null)
            {
                videoCore = new FFmpegVideoCore(protocol + "://" + StreamUrl);
                videoCore.OnVideoReceived   += videoCore_OnVideoReceived;
                videoCore.OnLogReceived     += videoCore_OnLogReceived;
            }
            else
            {
                videoCore.changeUrl(protocol + "://" + StreamUrl);
            }

            InfoVisible = Visibility.Visible;
            videoCore.Start();
            infoTimer.Start();
            refreshTimer.Start();
        }

        [RelayCommand]
        public void ButtonStop()
        {
            IsButtonOpenEnabled = true;
            isDrawing = false;
            refreshTimer.Stop();

            if (videoCore != null)
            {
                videoCore.Stop();

                // 2024.6.5 Blackcat: Add Blank Frame To Clear The Video
                ReceiveArgs = null;
                frontendCanvas.InvalidateVisual();

                InfoVisible = Visibility.Hidden;
                infoTimer.Stop();
            }
        }

        SettingWindow settingWindow;

        [RelayCommand]
        public void MenuOpenSettings()
        {
            settingWindow = new SettingWindow();
            settingWindow.Show();
        }

        [RelayCommand]
        public void MenuExit()
        {
            Application.Current.Shutdown();
        }
       
        [RelayCommand]
        public void MenuAbout()
        {
            MessageBox.Show($"Version: {App.ver}\nAuthor: XOT(minexo79)\nMail: minexo79@gmail.com", "About", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        public void MenuOpenRepo()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/minexo79/GabaLiveView",
                UseShellExecute = true
            });
        }

        [RelayCommand]
        public void ButtonHideTopBar()
        {
            TopBarVisible = Visibility.Collapsed;
            TopButtonVisible = Visibility.Visible;
        }

        [RelayCommand]
        public void ButtonShowTopBar()
        {
            TopBarVisible = Visibility.Visible;
            TopButtonVisible = Visibility.Hidden;
        }

        [RelayCommand]
        public void ButtonPhotoClick()
        {
            ShowNotifyMessage("Photo Captured");
        }

        [RelayCommand]
        public void ButtonRecordClick()
        {
            if (VideoRecording == "OFF")
            {
                VideoRecording = "ON";
                ShowNotifyMessage("Recording Started");
            }
            else
            {
                VideoRecording = "OFF";
                ShowNotifyMessage("Recording Stopped");
            }
        }

        private void videoCore_OnVideoReceived(object? sender, VideoReceiveArgs e)
        {
            ReceiveArgs = e;
            isDrawing = true;
        }

        private void videoCore_OnLogReceived(object? sender, LogArgs e)
        {
            LogMessage = e.logMessage ?? "";
        }

        private void ShowNotifyMessage(string msg)
        {
            // show notify message
            NotifyMessage   = msg;
            NotifyVisible   = Visibility.Visible;

            // hide notify message after 1.5 seconds
            Task.Delay(1500).ContinueWith(t =>
            {
                NotifyVisible = Visibility.Hidden;
            });
        }
    }
}
