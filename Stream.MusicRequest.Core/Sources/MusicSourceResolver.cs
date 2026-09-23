namespace Stream.MusicRequest.Core.Sources;

public class MusicSourceResolver : IMusicSourceResolver
{
    private readonly IReadOnlyList<IMusicSource> _sources;

    public MusicSourceResolver(IEnumerable<IMusicSource> sources)
    {
        _sources = sources.ToList();
    }

    public IMusicSource? Resolve(string url)
    {
        foreach (var source in _sources)
        {
            if (source.CanHandleUrl(url))
            {
                return source;
            }
        }

        return null;
    }
}
