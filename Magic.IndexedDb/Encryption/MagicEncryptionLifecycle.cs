namespace Magic.IndexedDb;

public enum MagicEncryptionState
{
    Uninitialized,
    Locked,
    Unlocked,
    PolicyMismatch,
    Inactive
}

public enum MagicUnlockResult
{
    Unlocked,
    AlreadyUnlocked,
    InteractionRequired,
    Cancelled,
    KeyUnavailable,
    InvalidKey,
    Uninitialized,
    PolicyMismatch
}

/// <summary>A provider can use this exception to defer an unlock until the app can obtain user interaction.</summary>
public sealed class MagicEncryptionInteractionRequiredException() : InvalidOperationException("The key provider requires user interaction.");

/// <summary>Optional richer status and unlock operations for a Magic encryption context.</summary>
public interface IMagicIndexedDbEncryptionLifecycle : IMagicIndexedDbEncryption
{
    Task<MagicEncryptionState> GetStateAsync(IndexedDbSet database, CancellationToken cancellationToken = default);
    /// <summary>Requests an unlock and reports the outcome of this caller's wait.</summary>
    /// <remarks>A cancelled wait can leave the provider running. Call LockAsync to invalidate the pending attempt.</remarks>
    Task<MagicUnlockResult> UnlockDetailedAsync(IndexedDbSet database, bool initialize = false,
        CancellationToken cancellationToken = default);
}
