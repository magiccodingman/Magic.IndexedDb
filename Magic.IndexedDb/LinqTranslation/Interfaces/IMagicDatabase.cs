using System.Text.Json;
using Magic.IndexedDb.Interfaces;

namespace Magic.IndexedDb.LinqTranslation.Interfaces;

public interface IMagicDatabaseScoped
{
    Task DeleteAsync();
    Task CloseAsync();

    Task<bool> IsOpenAsync();
    Task OpenAsync();
    Task<bool> DoesExistAsync();
    /// <summary>Queries a table registered in this schema and physical instance.</summary>
    ValueTask<IMagicQuery<T>> Query<T>() where T : class, IMagicTableBase, new();
    /// <summary>Reads migration metadata without creating or upgrading the database.</summary>
    Task<JsonElement?> InspectMigrationAsync(CancellationToken cancellationToken = default);
    /// <summary>Retries a failed initialization or resumes from its committed checkpoint.</summary>
    Task RetryMigrationAsync(CancellationToken cancellationToken = default);
    /// <summary>Cancels this tab's pending migration at a safe transaction boundary.</summary>
    Task CancelMigrationAsync();
    /// <summary>Explicitly executes a manifest-approved recovery, returning its outcome and committed revision.</summary>
    /// <remarks>Authenticated databases request authorization from the configured key provider without rerunning the failed original migration.
    /// The token cancels the .NET wait. Use CancelMigrationAsync to cancel browser work, or LockAsync on the encryption lifecycle to revoke authorization.</remarks>
    Task<JsonElement> RecoverMigrationAsync(string recoveryId, string sha256, CancellationToken cancellationToken = default);
}
