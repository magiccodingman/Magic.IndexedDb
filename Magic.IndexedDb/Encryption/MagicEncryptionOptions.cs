using Microsoft.JSInterop;

namespace Magic.IndexedDb;

/// <summary>Associates a logical Magic database with an account-specific authenticated browser database.</summary>
/// <remarks>Keep this configuration when a managed migration removes the last protected field.
/// The authenticated account/database identity remains stable while record protection changes.</remarks>
public sealed record MagicEncryptionOptions(
    string DatabaseName,
    string AccountScope,
    string KeyId,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>>? RecordPolicies = null)
{
    /// <summary>Explicitly binds a managed account context to an existing physical database without moving its records.</summary>
    /// <remarks>Use a reviewed migration history/baseline and retire older writers before enrollment.
    /// Retain this binding after enrollment, including when every record field becomes public again.</remarks>
    public string? PhysicalDatabaseName { get; init; }
}

public sealed record MagicEncryptionKeyRequest(string DatabaseName, string AccountScope, string KeyId);

/// <summary>Supplies a key prepared by the application, without coupling storage to authentication.</summary>
public interface IMagicEncryptionKeyProvider
{
    ValueTask<MagicEncryptionKey?> GetKeyAsync(MagicEncryptionKeyRequest request, CancellationToken cancellationToken);
}

public sealed class MagicEncryptionKey
{
    internal object Material { get; }

    private MagicEncryptionKey(object material) => Material = material;

    /// <summary>References the provider's 32-byte data key for immediate import.</summary>
    public static MagicEncryptionKey FromBytes(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 32)
            throw new ArgumentException("Magic encryption requires 32 key bytes.", nameof(key));

        return new MagicEncryptionKey(key);
    }

    /// <summary>References a browser-local AES-GCM CryptoKey. The provider owns the reference.</summary>
    public static MagicEncryptionKey FromBrowserKey(IJSObjectReference key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new MagicEncryptionKey(key);
    }
}

public interface IMagicIndexedDbEncryption
{
    /// <summary>Explicitly provisions a protected database or enrolls an approved unprotected baseline, then applies its managed target.</summary>
    Task<bool> InitializeAsync(IndexedDbSet database, CancellationToken cancellationToken = default);

    /// <summary>Unlocks an existing database and applies its configured authenticated migrations. False means the provider supplied no key.</summary>
    /// <remarks>Cancelling the caller's token cancels its wait only. Call <see cref="LockAsync"/> to invalidate the pending attempt.</remarks>
    Task<bool> EnsureUnlockedAsync(IndexedDbSet database, CancellationToken cancellationToken = default);

    Task LockAsync(IndexedDbSet database);
}
