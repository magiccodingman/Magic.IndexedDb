namespace Magic.IndexedDb.Models;

/// <summary>
/// Thrown when constructor metadata is ambiguous for a persisted type.
/// </summary>
public sealed class MagicConstructorException(string message) : InvalidOperationException(message);
