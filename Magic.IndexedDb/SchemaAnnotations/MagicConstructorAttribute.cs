namespace Magic.IndexedDb.SchemaAnnotations;

/// <summary>
/// Selects the constructor Magic IndexedDB uses when materializing a stored object.
/// </summary>
/// <remarks>
/// This attribute controls the database serialization contract. When it is absent,
/// <see cref="System.Text.Json.Serialization.JsonConstructorAttribute"/> remains supported
/// for compatibility.
/// </remarks>
[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
public sealed class MagicConstructorAttribute : Attribute;
