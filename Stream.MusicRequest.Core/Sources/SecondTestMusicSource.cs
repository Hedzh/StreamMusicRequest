using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Sources;

public class SecondTestMusicSource : IMusicSource
{
    public string Name => "Second Test Music Source";

    public bool CanHandleUrl(string url)
    {
        return url.StartsWith(
            "https://second.music/",
            StringComparison.OrdinalIgnoreCase);
    }

    public Task<Track?> ResolveTrackAsync(string url)
    {
        if (!CanHandleUrl(url))
        {
            return Task.FromResult<Track?>(null);
        }

        var track = new Track
        {
            Id = "second-test-track-001",
            Title = "Р’С‚РѕСЂРѕР№ С‚РµСЃС‚РѕРІС‹Р№ С‚СЂРµРє",
            Artist = "Р’С‚РѕСЂРѕР№ С‚РµСЃС‚РѕРІС‹Р№ РёСЃРїРѕР»РЅРёС‚РµР»СЊ",
            Album = "Р’С‚РѕСЂРѕР№ С‚РµСЃС‚РѕРІС‹Р№ Р°Р»СЊР±РѕРј",
            Duration = TimeSpan.FromMinutes(4),
            CoverUrl = null,
            SourceUrl = url
        };

        return Task.FromResult<Track?>(track);
    }
}
