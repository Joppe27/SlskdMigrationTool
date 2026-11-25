using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using slskd.Search;
using slskd.Search.API;
using slskd.Transfers.API;
using Soulseek;
using Search = slskd.Search.Search;

namespace SlskdMigrationTool.App;

public partial class SearchPanel : UserControl
{
    private readonly HttpClient _slskdClient = App.ServiceProvider.GetRequiredService<HttpClient>();

    private readonly List<Album> _skippedAlbums = new(); // TODO

    public SearchPanel()
    {
        InitializeComponent();
    }

    public async Task Search(IStorageFolder libraryFolder, SearchOptions searchOptions)
    {
        var albumFolders = new DirectoryInfo(libraryFolder.Path.AbsolutePath).EnumerateDirectories().ToList();

        List<Album> albums = new();
        foreach (var albumFolder in albumFolders)
            albums.Add(new Album(albumFolder));

        foreach (Album album in albums)
        {
            var searchId = await StartSearch(album.FolderNameClean, searchOptions);

            var responses = await AwaitSearchResponses(searchId, searchOptions);

            if (responses.Count == 0)
            {
                ConsoleText.Text += $"No responses received, skipping album: {album.FolderNameClean}\n";
                _skippedAlbums.Add(album);
                return;
            }

            if (searchOptions.PreferFastestUser)
                responses = responses.OrderBy(r => r.UploadSpeed).ToList();
            var validatedReponse = responses.FirstOrDefault(r =>
                r.QueueLength < 20 && // TODO: configurable?
                r.Files.All(f => !f.IsLocked) &&
                r.Files.Count(f => f.Filename.EndsWith(searchOptions.Codec.ToString(), StringComparison.InvariantCultureIgnoreCase)) >= (searchOptions.MatchFileCount ? album.Tracks.Count : 0) &&
                r.Files.Where(f => f.Filename.EndsWith(searchOptions.Codec.ToString(), StringComparison.InvariantCultureIgnoreCase)).All(f => f.BitRate >= searchOptions.MinBitrate));

            if (validatedReponse is null)
            {
                ConsoleText.Text += $"None of {responses.Count} responses match migration settings, skipping album: {album.FolderNameClean}\n";
                return;
            }

            await DownloadAlbum(validatedReponse, searchId, album, searchOptions);
        }
    }

    private async Task<Guid> StartSearch(string albumName, SearchOptions searchOptions)
    {
        SearchRequest searchRequest = new()
        {
            SearchText = albumName,
            SearchTimeout = searchOptions.SearchTimeout * 1000,
            MinimumResponseFileCount = searchOptions.SearchMinResponses,
            ResponseLimit = searchOptions.SearchMaxResponses,
        };

        var searchResponse = await _slskdClient.PostAsync("/api/v0/Searches", JsonContent.Create(searchRequest));
        searchResponse.EnsureSuccessStatusCode();

        var searchContent = await searchResponse.Content.ReadFromJsonAsync<Search>(new JsonSerializerOptions()
            { Converters = { new JsonStringEnumConverter<SearchStates>() }, PropertyNameCaseInsensitive = true }) ?? throw new NullReferenceException();
        var searchId = searchContent.Id;

        return searchId;
    }

    private async Task<List<Response>> AwaitSearchResponses(Guid searchId, SearchOptions searchOptions)
    {
        Search? search = null;

        var sw = new Stopwatch();
        sw.Start();

        while (search is null || !search.IsComplete)
        {
            var searchGetRequest = await _slskdClient.GetAsync($"/api/v0/Searches/{searchId.ToString()}");
            searchGetRequest.EnsureSuccessStatusCode();
            search = await searchGetRequest.Content.ReadFromJsonAsync<Search>(new JsonSerializerOptions()
                { Converters = { new JsonStringEnumConverter<SearchStates>() }, PropertyNameCaseInsensitive = true }) ?? throw new NullReferenceException();

            if (search.IsComplete)
                break;

            ConsoleText.Text += $"Waiting on search to complete: {search.SearchText} ({sw.Elapsed.TotalSeconds} seconds elapsed)\n";

            if (sw.ElapsedMilliseconds > searchOptions.SearchTimeout * 1000)
            {
                ConsoleText.Text += "Search timeout reached, using only responses received thus far\n";

                var searchStopRequest = await _slskdClient.PutAsync($"/api/v0/Searches/{searchId.ToString()}", null);
                searchStopRequest.EnsureSuccessStatusCode();

                break;
            }

            await Task.Delay(1000);
        }

        sw.Stop();

        var responsesRequest = await _slskdClient.GetAsync($"/api/v0/Searches/{searchId.ToString()}/responses");
        responsesRequest.EnsureSuccessStatusCode();

        var responses = await responsesRequest.Content.ReadFromJsonAsync<IEnumerable<Response>>(new JsonSerializerOptions()
            { Converters = { new JsonStringEnumConverter<SearchStates>() }, PropertyNameCaseInsensitive = true }) ?? throw new NullReferenceException();

        return responses.ToList();
    }

    private async Task DownloadAlbum(Response userResponse, Guid associatedSearch, Album album, SearchOptions searchOptions)
    {
        List<EnqueueDownloadBatchItem> albumFilesToDownload = new();

        foreach (var file in userResponse.Files)
        {
            if (!searchOptions.ReplaceExisting && album.TracksInSlskdDownloads.Contains(file, EqualityComparer<slskd.Search.File>.Create((a, b) =>
                {
                    if (a is null || b is null)
                        return false;

                    return Math.Abs(a.Size - b.Size) < 100 * 1024;
                })))
            {
                ConsoleText.Text += $"File already present in slskd downloads folder, skipping track: {file.Filename}\n";
                continue;
            }

            if (!file.Filename.EndsWith(searchOptions.Codec.ToString(), StringComparison.InvariantCultureIgnoreCase))
            {
                ConsoleText.Text += $"File not audio or wrong codec, skipping: {file.Filename}"; // TODO: configurable
                continue;
            }

            albumFilesToDownload.Add(new EnqueueDownloadBatchItem()
            {
                Filename = file.Filename,
                Size = file.Size,
            });
        }

        var downloadRequest = new EnqueueDownloadBatchRequest()
        {
            SearchId = associatedSearch.ToString(),
            Username = userResponse.Username,
            Files = albumFilesToDownload,
            Options = new EnqueueDownloadBatchOptions() { Destination = album.FolderName }
        };

        await _slskdClient.PostAsync("api/v0/Transfers/downloads/batches", JsonContent.Create(downloadRequest)); // TODO: check request
        ConsoleText.Text += $"Downloads enqueued: {downloadRequest.Files.Count} files will be downloaded to the {album.FolderName} folder\n";
    }

    public class Album
    {
        public string FolderName { get; }
        public string FolderNameClean { get; }
        public List<slskd.Search.File> Tracks { get; } = new();

        public List<slskd.Search.File> TracksInSlskdDownloads = new(); // TODO 

        readonly string[] _countryCodes =
        [
            "US", "UK", "EU", "JP", "DE", "FR", "IT", "ES", "NL", "BE", "SE", "NO",
            "DK", "FI", "PL", "AT", "CH", "CA", "AU", "NZ", "BR", "MX", "KR", "CN",
            "RU", "IE", "PT", "GR", "CZ", "HU",
        ];

        readonly string[] _noiseWords =
        [
            "INTL", "CD", "VER", "VERSION", "SINGLE", "DELUXE", "REMASTER",
            "REMASTERED", "EDITION", "ANNIVERSARY", "REISSUE", "BONUS", "DISC",
            "DISC1", "DISC2", "OST", "EP", "LP",
        ];

        public Album(DirectoryInfo albumFolder)
        {
            FolderName = albumFolder.Name;
            FolderNameClean = CleanFolderName(albumFolder.Name);

            var albumFiles = albumFolder.EnumerateFiles("*", new EnumerationOptions() { AttributesToSkip = FileAttributes.Hidden });
            foreach (var file in albumFiles)
            {
                Tracks.Add(new slskd.Search.File()
                {
                    Filename = file.Name,
                    Size = file.Length,
                });
            }
        }

        private string CleanFolderName(string folderName)
        {
            folderName = Regex.Replace(folderName, "\\([^)]*\\)", "");
            folderName = Regex.Replace(folderName, "\\[[^\\]]*\\]", "");
            folderName = folderName.Replace('-', ' ');

            foreach (string country in _countryCodes)
                folderName = folderName.Replace($" {country} ", "");

            foreach (string word in _noiseWords)
                folderName = folderName.Replace($" {word} ", "");

            return folderName;
        }
    }

    public record SearchOptions(
        int SearchTimeout,
        int SearchMinResponses,
        int SearchMaxResponses,
        bool MatchFileCount,
        bool ReplaceExisting,
        bool PreferFastestUser,
        AudioCodec Codec,
        int MinBitrate);

    public enum AudioCodec
    {
        MP3,
        AAC,
        Opus,
        FLAC,
        ALAC,
    }
}