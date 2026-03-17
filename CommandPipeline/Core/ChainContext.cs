namespace CommandPipeline.Core;

public class ChainContext
{
    private readonly Dictionary<string, object?> _data = new();

    public bool IsFailed { get; private set; }
    public List<Exception> Errors { get; } = new();
    public CancellationToken CancellationToken { get; init; }

    public void Set<T>(string key, T value) => _data[key] = value;
    public T? Get<T>(string key) => _data.TryGetValue(key, out var v) ? (T?)v : default;

    public void Fail(Exception ex)
    {
        IsFailed = true; Errors.Add(ex);
    }
}
