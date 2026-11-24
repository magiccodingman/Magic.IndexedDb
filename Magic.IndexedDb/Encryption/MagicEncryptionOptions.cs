using Microsoft.JSInterop;

namespace Magic.IndexedDb;

/// <summary>Associates a logical Magic database with an account-specific protected browser database.</summary>
public sealed record MagicEncryptionOptions(
    string DatabaseName,
    string AccountScope,
    string KeyId,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>>? RecordPolicies = null);

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
    /// <summary>Explicitly provisions an empty protected database and unlocks it.</summary>
    Task<bool> InitializeAsync(IndexedDbSet database, CancellationToken cancellationToken = default);

    /// <summary>Unlocks an existing database. False means the provider supplied no key.</summary>
    Task<bool> EnsureUnlockedAsync(IndexedDbSet database, CancellationToken cancellationToken = default);

    Task LockAsync(IndexedDbSet database);
}
