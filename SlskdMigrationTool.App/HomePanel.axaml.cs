// Copyright (c) Joppe27 <joppe27.be>. Licensed under the MIT Licence.
// See LICENSE file in repository root for full license text.

#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using slskd;
using Soulseek;

#endregion

namespace SlskdMigrationTool.App;

public partial class HomePanel : UserControl
{
    private IStorageFolder _albumFolder = null!;
    private IStorageFolder _slskdDownloadsFolder = null!;

    private readonly HttpClient _testClient = new();
    private bool _slskdConnected = false;

    private MainWindow _mainWindow = null!;

    public List<string> AudioCodecs { get; } = Enum.GetNames(typeof(SearchPanel.AudioCodec)).ToList();

    public HomePanel()
    {
        InitializeComponent();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        
        if (TopLevel.GetTopLevel(this) is MainWindow mainWindow)
            _mainWindow = mainWindow;
        else
            throw new InvalidOperationException();
        
        DataContext = this;
    }

    private void BrowseAlbumFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = TaskUtilities.AwaitNotify(this, async () =>
        {
            var pickerFolder = await _mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
            {
                AllowMultiple = false,
                Title = "Select existing album folder..."
            });

            if (pickerFolder.Count == 0)
                return;

            _albumFolder = pickerFolder.First();
            AlbumFolderTextBlock.Text = _albumFolder.Path.LocalPath;
        });
    }

    private void SearchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (CodecComboBox.SelectedValue is string codecName && Enum.TryParse(codecName, false, out SearchPanel.AudioCodec audioCodec) &&
            Int32.TryParse(BitrateTextBox.Text, out int minBitrate))
        {
            var searchPanel = new SearchPanel();
        
            _ = searchPanel.Search(_albumFolder, _slskdDownloadsFolder,
                    new SearchPanel.SearchOptions((int)SearchTimeoutBox.Value!, (int)SearchMinResponseBox.Value!, (int)SearchMaxResponseBox.Value!, MatchFileCountCheckBox.IsChecked ?? false,
                        ReplaceDownloadsCheckBox.IsChecked ?? false, PreferFastestUploadCheckBox.IsChecked ?? false, audioCodec, minBitrate));
        
            if (this.Parent is Panel flexPanel)
            {
                _mainWindow.HomePanelSavedState = this;
            
                flexPanel.Children.Clear();
                flexPanel.Children.Add(searchPanel);
            }
            else
            {
                throw new InvalidOperationException();
            }
        }
        else
        {
            SearchButtonFlyoutText.Text = "Invalid input!";
            FlyoutBase.ShowAttachedFlyout(SearchButton);
        }
    }

    private void ConnectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = TaskUtilities.AwaitNotify(this, async () =>
        {
            if (SlskdPortTextBox.Text is null || SlskdKeyTextBox.Text is null || _slskdConnected)
                return;

            var req = new HttpRequestMessage(HttpMethod.Get, SlskdPortTextBox.Text + "api/v0/Application");
            req.Headers.Add("X-API-Key", SlskdKeyTextBox.Text);

            var applicationRequest = await _testClient.SendAsync(req);
            if (!applicationRequest.IsSuccessStatusCode)
                return;

            var applicationState = await applicationRequest.Content.ReadFromJsonAsync<State>(new JsonSerializerOptions()
                { Converters = { new JsonStringEnumConverter() }, PropertyNameCaseInsensitive = true }); // TODO
            if (applicationState is null || !applicationState.Server.IsConnected)
                return;

            _slskdConnected = true;
            Console.WriteLine("Connection verified!");

            HttpClient slskdClient = App.ServiceProvider.GetRequiredService<HttpClient>();
            slskdClient.BaseAddress = new Uri(SlskdPortTextBox.Text);
            slskdClient.DefaultRequestHeaders.Add("X-API-Key", SlskdKeyTextBox.Text);

            var checkSvg = Avalonia.Svg.SvgSource.Load("avares://SlskdMigrationTool.App/Icons/circle-check-regular-purple.svg", null);
            ConnectButton.Content = new Image() { Source = new Avalonia.Svg.SvgImage() { Source = checkSvg }, MaxHeight = 36 };
            ConnectButton.IsEnabled = false;

            SlskdPortTextBox.IsReadOnly = true;
            SlskdKeyTextBox.IsReadOnly = true;

            var optionsRequest = await slskdClient.GetAsync("api/v0/Options/startup");
            optionsRequest.EnsureSuccessStatusCode();

            var options = await optionsRequest.Content.ReadFromJsonAsync<Options>(new JsonSerializerOptions()
                    { Converters = { new JsonStringEnumConverter() }, PropertyNameCaseInsensitive = true });
            if (options is { } startupOptions && await _mainWindow.StorageProvider.TryGetFolderFromPathAsync(startupOptions.Directories.Downloads) is
                    { } downloadsFolder)
                _slskdDownloadsFolder = downloadsFolder;
            else
                throw new InvalidOperationException();

            SlskdDownloadFolderTextBlock.Text = SlskdDownloadFolderTextBlock.Text!.Replace("n/a", _slskdDownloadsFolder.Path.LocalPath);

            SearchButton.IsEnabled = true;
        });
    }

    private void SearchButton_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (SearchButton.IsEnabled)
            return;

        SearchButtonFlyoutText.Text = "Connect to slskd first!";
        FlyoutBase.ShowAttachedFlyout(SearchButton); // TODO: broken?
    }
}