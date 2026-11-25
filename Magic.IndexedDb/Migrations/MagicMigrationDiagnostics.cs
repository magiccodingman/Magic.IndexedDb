using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Magic.IndexedDb.Migrations;

/// <summary>A versioned progress notification with no records, keys, or custom exception text.</summary>
public sealed record MagicMigrationEvent
{
    public int ProtocolVersion { get; init; }
    public string? Name { get; init; }
    public string? SchemaId { get; init; }
    public string Phase { get; init; } = "";
    public string? Code { get; init; }
    public bool Retryable { get; init; }
    public int? Revision { get; init; }
    public int? TargetRevision { get; init; }
    public string? MigrationId { get; init; }
    public long? ProcessedRecords { get; init; }
    public string? DocumentationUrl { get; init; }
    /// <summary>Future metadata fields are preserved without changing this event contract.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Details { get; init; }
}

internal sealed class MagicMigrationSubscription : IAsyncDisposable
{
    private readonly IJSObjectReference _module;
    private Func<MagicMigrationEvent, ValueTask>? _observer;
    private readonly Action<string> _onDispose;
    private DotNetObjectReference<MagicMigrationSubscription>? _reference;
    internal string Id { get; } = Guid.NewGuid().ToString("N");

    internal MagicMigrationSubscription(IJSObjectReference module, Func<MagicMigrationEvent, ValueTask> observer, Action<string> onDispose)
    {
        _module = module;
        _observer = observer;
        _onDispose = onDispose;
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        _reference = DotNetObjectReference.Create(this);
        try { await _module.InvokeVoidAsync("observeMigrations", cancellationToken, Id, _reference); }
        catch { await DisposeAsync(); throw; }
    }

    [JSInvokable]
    public async Task OnMigrationEvent(MagicMigrationEvent notification)
    {
        var observer = Volatile.Read(ref _observer);
        if (observer is null) return;
        try { await observer(notification); }
        catch { /* Observability must not control the persistence protocol. */ }
    }

    public async ValueTask DisposeAsync()
    {
        var reference = Interlocked.Exchange(ref _reference, null);
        if (reference is null) return;
        Interlocked.Exchange(ref _observer, null);
        _onDispose(Id);
        try { await _module.InvokeVoidAsync("stopObservingMigrations", Id); }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        finally { reference.Dispose(); }
    }
}
