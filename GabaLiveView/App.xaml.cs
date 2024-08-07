﻿using GabaLiveView.Utilities;
using System.Configuration;
using System.Data;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace GabaLiveView
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // ====================
        // hide console window
        // https://stackoverflow.com/questions/3571627/show-hide-the-console-window-of-a-c-sharp-console-application
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public static int SW_HIDE = 0;
        public static int SW_SHOW = 5;

        // app version
        public static string ver = Assembly.GetExecutingAssembly().GetName().Version.ToString();

        // Ini File
        static IniOpreration iniOpreration = new IniOpreration();
        public static int StreamProtocol { get; set; } = 0;
        public static string StreamUrl { get; set; } = String.Empty;

        public static string SavePath { get; set; } = String.Empty;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            Console.WriteLine($"Version: {ver}");

            IniCheck();
            HideConsole();
        }

        private void HideConsole()
        {

            var handle = GetConsoleWindow();
            ShowWindow(handle, SW_HIDE);
        }

        private void IniCheck()
        {
            string proto = iniOpreration.Read("StreamProtocol");
            StreamUrl = iniOpreration.Read("StreamUrl");
            SavePath = iniOpreration.Read("SavePath");

            if (proto == String.Empty)
            {
                iniOpreration.Write("StreamProtocol", "0");
            }
            else
            {
                StreamProtocol = int.Parse(proto);
            }

            if (StreamUrl == String.Empty)
            {
                StreamUrl = "192.168.1.1:554/hello";
                iniOpreration.Write("StreamUrl", StreamUrl);
            }

            if (SavePath == String.Empty)
            {
                SavePath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\GabaSave";
                iniOpreration.Write("SavePath", SavePath);
            }
        }

        internal static void SaveIni()
        {
            iniOpreration.Write("StreamProtocol", StreamProtocol.ToString());
            iniOpreration.Write("StreamUrl", StreamUrl);
            iniOpreration.Write("SavePath", SavePath);
        }

        internal static void SaveIni(int streamProto, string streamUrl, string savePath)
        {
            iniOpreration.Write("StreamProtocol", streamProto.ToString());
            iniOpreration.Write("StreamUrl", streamUrl);
            iniOpreration.Write("SavePath", savePath);
        }
    }
}
