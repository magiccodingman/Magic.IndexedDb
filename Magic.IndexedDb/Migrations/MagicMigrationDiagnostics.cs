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
    public string? RecoveryId { get; init; }
    public string? RecoveryPhase { get; init; }
    public long? VerifiedRecords { get; init; }
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
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _module.InvokeVoidAsync("observeMigrations", cancellationToken, Id, _reference);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    [JSInvokable("OnMigrationEvent")]
    public Task ReceiveMigrationEvent(JsonElement payload)
    {
        var metadata = payload.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        JsonElement Take(string name) => metadata.Remove(name, out var value) ? value : default;
        string? Text(string name) => Take(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
        int? Int(string name) => Take(name) is { ValueKind: JsonValueKind.Number } value ? value.GetInt32() : null;
        long? Long(string name) => Take(name) is { ValueKind: JsonValueKind.Number } value ? value.GetInt64() : null;

        return OnMigrationEvent(new MagicMigrationEvent
        {
            ProtocolVersion = Int("protocolVersion") ?? 0,
            Name = Text("name"),
            SchemaId = Text("schemaId"),
            Phase = Text("phase") ?? "",
            Code = Text("code"),
            Retryable = Take("retryable").ValueKind == JsonValueKind.True,
            Revision = Int("revision"),
            TargetRevision = Int("targetRevision"),
            MigrationId = Text("migrationId"),
            RecoveryId = Text("recoveryId"),
            RecoveryPhase = Text("recoveryPhase"),
            VerifiedRecords = Long("verifiedRecords"),
            ProcessedRecords = Long("processedRecords"),
            DocumentationUrl = Text("documentationUrl"),
            Details = metadata.Count == 0 ? null : metadata
        });
    }

    internal async Task OnMigrationEvent(MagicMigrationEvent notification)
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
