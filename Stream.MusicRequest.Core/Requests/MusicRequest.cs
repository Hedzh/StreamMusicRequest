namespace Stream.MusicRequest.Core.Requests;

public class MusicRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ViewerName { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
