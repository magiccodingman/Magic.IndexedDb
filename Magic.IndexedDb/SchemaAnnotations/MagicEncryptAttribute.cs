namespace Magic.IndexedDb.SchemaAnnotations;

/// <summary>Protects a persisted top-level property with the database's configured data key.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MagicEncryptAttribute : Attribute;
