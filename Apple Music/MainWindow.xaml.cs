﻿using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Windows.ApplicationModel.Email;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Apple_Music.Models;

namespace Apple_Music
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Variables
        
        private readonly DataViewModel _dvm = new DataViewModel();
        private readonly DiscordRichPresence _rpc = new DiscordRichPresence();
        public static readonly RoutedCommand SettingsCommand = new RoutedCommand();
        
        #endregion
        
        public MainWindow()
        {
            InitializeComponent();
            DataContext = _dvm;
            CheckValidSettings();
            SettingsCommand.InputGestures.Add(new KeyGesture(Key.OemComma, ModifierKeys.Control));
            Settings.Click += OpenSettings;
            Lyrics.Click += ToggleLyricsPanel;
            InitializeWebView();
        }

        private void CheckValidSettings()
        {
            var didSettingsChange = false;
            if (Properties.Settings.Default.DiscordRpcFirstLine == "")
            {
                Properties.Settings.Default.DiscordRpcFirstLine = "🎵 song";
                didSettingsChange = true;
            }
            if (Properties.Settings.Default.DiscordRpcSecondLine == "")
            {
                Properties.Settings.Default.DiscordRpcSecondLine = "🎤 artist 💽 album";
                didSettingsChange = true;
            }
            if (Properties.Settings.Default.WebUrl == "")
            {
                Properties.Settings.Default.WebUrl = "https://music.apple.com/";
                didSettingsChange = true;
            }
            if (didSettingsChange) Properties.Settings.Default.Save();
        }

        private async void InitializeWebView()
        {
            // Initialize WebView
            await AmWebView.EnsureCoreWebView2Async();
            // Go to pre-saved url
            AmWebView.Source = new Uri(Properties.Settings.Default.WebUrl);
            // Change window title when document title changes
            AmWebView.CoreWebView2.DocumentTitleChanged += AppleMusic_TitleChanged;
            // Remove extra shit from the website (like the Open in iTunes button) when page loads
            AmWebView.CoreWebView2.SourceChanged += AppleMusic_RemoveExtraElements;
            AmWebView.CoreWebView2.NavigationCompleted += AppleMusic_RemoveExtraElements;
            // Start getting data from now playing
            //webView.CoreWebView2.SourceChanged += AppleMusic_InitDiscordRPC;
            AmWebView.CoreWebView2.NavigationCompleted += InitDiscordRpc;
            // Receive messages from webapp
            AmWebView.CoreWebView2.WebMessageReceived += UpdateRichPresence;
        }
        
        private void AppleMusic_TitleChanged(object sender, object e) => _dvm.WindowTitle = AmWebView.CoreWebView2.DocumentTitle;

        private async void AppleMusic_RemoveExtraElements(object sender, object e)
        {
            await RunJsCode("const elements = document.getElementsByClassName('web-navigation__native-upsell'); while (elements.length > 0) elements[0].remove();");
            await RunJsCode("while (elements.length > 0) elements[0].remove();");
        }
        
        private async void InitDiscordRpc(object sender, object e)
        {
            // yeah...
            _rpc.Initialize();
            // Get playing state
            // Credit: https://github.com/iiFir3z/Apple-Music-Electron/
            await RunJsCode(
                "MusicKit.getInstance().addEventListener( MusicKit.Events.playbackStateDidChange, (a) => {" +
                "window.chrome.webview.postMessage({state: a.state});" +
                "});");
            // Get music data. I'm so sorry for making you see this
            // Credit: https://github.com/iiFir3z/Apple-Music-Electron/
            await RunJsCode(
                "MusicKit.getInstance().addEventListener( MusicKit.Events.mediaItemStateDidChange, function() {" +
                "const nowPlayingItem =  MusicKit.getInstance().nowPlayingItem; let attributes  = {}; if (nowPlayingItem != null) { attributes = nowPlayingItem.attributes; }" +
                "attributes.name = attributes.name ? attributes.name : null;" +
                "attributes.durationInMillis = attributes.durationInMillis ? attributes.durationInMillis : 0;" +
                "attributes.albumName = attributes.albumName ? attributes.albumName : null;" +
                "attributes.artistName = attributes.artistName ? attributes.artistName : null;" +
                "window.chrome.webview.postMessage(attributes);" +
                "})");
            // Get lyrics
            await RunJsCode(
                "MusicKit.getInstance().addEventListener(MusicKit.Events.mediaItemStateDidChange, function() {" +
                "let attributes = {};" +
                "let mk = MusicKit.getInstance();" +
                "if (mk.nowPlayingItem != undefined) {" +
                "mk.api.lyric(mk.nowPlayingItem.songId).then(function(data) {" +
                // there are lyrics!
                "attributes.response = data.ttml; attributes.hasLyrics = true;" +
                "window.chrome.webview.postMessage(attributes);" +
                // error requesting lyrics = no lyrics
                "} ).catch(e => attributes.hasLyrics = false);" +
                // nowPlayingItem is undefined
                "} else { attributes.hasLyrics = false }" +
                "window.chrome.webview.postMessage(attributes);" +
                "})");
        }
        
        private void UpdateRichPresence(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (_dvm.IsDiscordRpcEnabled)
                _rpc.Initialize();
            else
            {
                _rpc.EndConnection();
                return;
            }
            
            // Check for lyrics
            var lyrics = JsonConvert.DeserializeObject<LyricResponse>(args.WebMessageAsJson);
            if (lyrics.HasLyrics != null)
            {
                if (lyrics.HasLyrics == true) 
                    _dvm.Lyrics = Utils.ParseLyrics(lyrics.Response);
                else 
                    _dvm.ShowLyrics = false;
                
                _dvm.EnableLyrics = lyrics.HasLyrics == true; // weird nullable shit
                return;
            }
            
            // Update discord rich presence
            var song = JsonConvert.DeserializeObject<SongResponse>(args.WebMessageAsJson);
            _rpc.UpdatePresence(song);
        }

        private async Task RunJsCode(string code) => await AmWebView.CoreWebView2.ExecuteScriptAsync(code);

        private void OpenSettings(object sender, object args) => new Settings().Show();

        private void OnClose(object sender, dynamic args) => _rpc.EndConnection();

        private void ToggleLyricsPanel(object sender, dynamic args) => _dvm.ShowLyrics = !_dvm.ShowLyrics;
    }
}
