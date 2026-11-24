namespace Magic.IndexedDb;

/// <summary>A protected database operation requires an active unlocked session.</summary>
public sealed class MagicEncryptionLockedException : InvalidOperationException
{
    internal MagicEncryptionLockedException(Exception innerException)
        : base("Unlock the Magic database before accessing a protected record.", innerException)
    {
    }
}
