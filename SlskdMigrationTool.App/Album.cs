using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SlskdMigrationTool.App;

public class Album
{
    public string Name { get; }
    public string FolderName { get; }
    public List<slskd.Search.File> TracksOnDisk { get; } = new();
    
    readonly string[] _countryCodes = [
        "US", "UK", "EU", "JP", "DE", "FR", "IT", "ES", "NL", "BE", "SE", "NO",
        "DK", "FI", "PL", "AT", "CH", "CA", "AU", "NZ", "BR", "MX", "KR", "CN",
        "RU", "IE", "PT", "GR", "CZ", "HU",
    ];
    readonly string[] _noiseWords = [
        "INTL", "CD", "VER", "VERSION", "SINGLE", "DELUXE", "REMASTER",
        "REMASTERED", "EDITION", "ANNIVERSARY", "REISSUE", "BONUS", "DISC",
        "DISC1", "DISC2", "OST", "EP", "LP",
    ];

    public Album(string folderName)
    {
        FolderName = folderName;
        Name = CleanFolderName(folderName);

        const string libraryDirectory = "V:/3. Muziek/Albums (FLAC)";
        string albumDirectory = Path.Combine(libraryDirectory, folderName);
        var files = new DirectoryInfo(albumDirectory).EnumerateFiles();
        foreach (var file in files)
        {
            TracksOnDisk.Add(new slskd.Search.File()
            {
                Filename = file.Name,
                Size = file.Length,
            });
        }
    }
    
    private string CleanFolderName(string album)
    {
        album = Regex.Replace(album, "\\([^)]*\\)", "");
        album = Regex.Replace(album, "\\[[^\\]]*\\]", "");
        album = album.Replace('-', ' ');

        foreach (string country in _countryCodes)
            album = album.Replace(country, "");

        foreach (string word in _noiseWords)
            album = album.Replace(word, "");

        return album;
    }
}