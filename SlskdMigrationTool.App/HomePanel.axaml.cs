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

    private readonly HttpClient _testClient = new();
    private bool _slskdConnected = false;

    public List<string> AudioCodecs { get; } = Enum.GetNames(typeof(SearchPanel.AudioCodec)).ToList();

    public HomePanel()
    {
        InitializeComponent();
        
        DataContext = this;
    }

    private async void BrowseAlbumFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } window)
        {
            var pickerFolder = await TaskUtilities.AwaitNotify(this, window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
            {
                AllowMultiple = false,
                Title = "Select existing album folder..."
            }));

            if (pickerFolder.Count == 0)
                return;

            _albumFolder = pickerFolder.First();
            AlbumFolderTextBlock.Text = _albumFolder.Path.ToString();
        }

        if (this.Parent is Panel flexPanel)
        {
            // try
            // {
            //     = await TaskUtilities.AwaitNotify(this, AnalyzePanel.CreateAsync());
            // }
            // catch (Exception ex)
            // {
            //     ExceptionUtilities.LogNotify(this, ex);
            //     return;
            // }
            //
            // flexPanel.Children.Clear();
            // flexPanel.Children.Add();
        }
    }

    private void SearchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (CodecComboBox.SelectedValue is string codecName && Enum.TryParse(codecName, false, out SearchPanel.AudioCodec audioCodec) &&
            Int32.TryParse(BitrateTextBox.Text, out int minBitrate))
        {
            var searchPanel = new SearchPanel();
            
            _ = TaskUtilities.AwaitNotify(this,
                searchPanel.Search(_albumFolder,
                    new SearchPanel.SearchOptions((int)SearchTimeoutBox.Value!, (int)SearchMinResponseBox.Value!, (int)SearchMaxResponseBox.Value!, MatchFileCountCheckBox.IsChecked ?? false,
                        ReplaceDownloadsCheckBox.IsChecked ?? false, PreferFastestUploadCheckBox.IsChecked ?? false, audioCodec, minBitrate)));
            
            if (this.Parent is Panel flexPanel)
            {
                if (TopLevel.GetTopLevel(this) is MainWindow mainWindow)
                    mainWindow.HomePanelSavedState = this;
                else
                    throw new InvalidOperationException();
                
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

    private async void ConnectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SlskdPortTextBox.Text is null || SlskdKeyTextBox.Text is null || _slskdConnected)
            return;

        var req = new HttpRequestMessage(HttpMethod.Get, SlskdPortTextBox.Text + "api/v0/Application");
        req.Headers.Add("X-API-Key", SlskdKeyTextBox.Text);

        var applicationRequest = await TaskUtilities.AwaitNotify(this, _testClient.SendAsync(req));
        if (!applicationRequest.IsSuccessStatusCode)
            return;

        var applicationState = await TaskUtilities.AwaitNotify(this, applicationRequest.Content.ReadFromJsonAsync<State>(new JsonSerializerOptions()
            { Converters = { new JsonStringEnumConverter() }, PropertyNameCaseInsensitive = true })); // TODO
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

        var optionsRequest = await TaskUtilities.AwaitNotify(this, slskdClient.GetAsync("api/v0/Options/startup"));
        optionsRequest.EnsureSuccessStatusCode();

        string slskdDownloadFolder;

        var options = await TaskUtilities.AwaitNotify(this,
            optionsRequest.Content.ReadFromJsonAsync<Options>(new JsonSerializerOptions()
                { Converters = { new JsonStringEnumConverter() }, PropertyNameCaseInsensitive = true }));
        if (options is { } startupOptions)
            slskdDownloadFolder = startupOptions.Directories.Downloads;
        else
            throw new InvalidOperationException();

        SlskdDownloadFolderTextBlock.Text = SlskdDownloadFolderTextBlock.Text!.Replace("n/a", slskdDownloadFolder);

        SearchButton.IsEnabled = true;
    }

    private void SearchButton_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (SearchButton.IsEnabled)
            return;

        var buttonBorder = sender as Control;
        if (buttonBorder is null)
            return;

        SearchButtonFlyoutText.Text = "Connect to slskd first!";
        FlyoutBase.ShowAttachedFlyout(SearchButton);
    }
}