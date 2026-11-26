namespace Magic.IndexedDb;

public enum MagicKeyRotationMode
{
    Exclusive,
    ReadOnly,
    Online
}

/// <summary>Controls availability and the bounded browser work per batch.</summary>
public sealed record MagicKeyRotationOptions(
    string TargetKeyId,
    MagicKeyRotationMode Mode = MagicKeyRotationMode.Exclusive,
    int BatchSize = 128,
    bool YieldBetweenBatches = false);

public enum MagicKeyRotationPhase
{
    NotStarted,
    AuthorizationRequired,
    Converting,
    Verifying,
    Completed
}

/// <summary>Rotation metadata. Counts reflect only committed work.</summary>
public sealed record MagicKeyRotationStatus(
    MagicKeyRotationPhase Phase,
    string? RotationId,
    string? SourceKeyId,
    string? TargetKeyId,
    MagicKeyRotationMode? Mode,
    bool Paused,
    long Converted,
    long Verified,
    bool ReadsAllowed,
    bool WritesAllowed,
    bool RetirementReady);

/// <summary>Opt in to dynamic generation lookup and attest restart recovery of both keys.</summary>
public interface IMagicEncryptionRotationKeyProvider : IMagicEncryptionKeyProvider
{
    /// <summary>
    /// Confirms the application can recover both keys after interruption.
    /// The browser can authenticate supplied keys but cannot verify future provider access.
    /// </summary>
    ValueTask<bool> CanReacquireKeysAsync(MagicEncryptionKeyRequest source, MagicEncryptionKeyRequest target,
        CancellationToken cancellationToken);
}

/// <summary>Controls rotation for an authorized managed database.</summary>
public interface IMagicIndexedDbKeyRotation
{
    Task<MagicKeyRotationStatus> RotateEncryptionKeyAsync(IndexedDbSet database, MagicKeyRotationOptions options,
        CancellationToken cancellationToken = default);
    Task<MagicKeyRotationStatus> ResumeKeyRotationAsync(IndexedDbSet database, int batchSize = 128,
        CancellationToken cancellationToken = default);
    Task<MagicKeyRotationStatus> GetKeyRotationStatusAsync(IndexedDbSet database,
        CancellationToken cancellationToken = default);
    Task<MagicKeyRotationStatus> PauseKeyRotationAsync(IndexedDbSet database, CancellationToken cancellationToken = default);
    Task CancelKeyRotationAsync(IndexedDbSet database);
}
