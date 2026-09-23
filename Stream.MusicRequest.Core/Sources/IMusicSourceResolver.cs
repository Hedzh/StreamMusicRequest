namespace Stream.MusicRequest.Core.Sources;

public interface IMusicSourceResolver
{
    IMusicSource? Resolve(string url);
}
