using Stream.MusicRequest.Core.Models;

namespace Stream.MusicRequest.Core.Sources;

public class TestMusicSource : IMusicSource
{
    public string Name => "Test Music Source";

    public bool CanHandleUrl(string url)
    {
        return url.StartsWith(
            "https://test.music/",
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
            Id = "test-track-001",
            Title = "РўРµСЃС‚РѕРІС‹Р№ С‚СЂРµРє",
            Artist = "РўРµСЃС‚РѕРІС‹Р№ РёСЃРїРѕР»РЅРёС‚РµР»СЊ",
            Album = "РўРµСЃС‚РѕРІС‹Р№ Р°Р»СЊР±РѕРј",
            Duration = TimeSpan.FromMinutes(3),
            CoverUrl = null,
            SourceUrl = url
        };

        return Task.FromResult<Track?>(track);
    }
}
