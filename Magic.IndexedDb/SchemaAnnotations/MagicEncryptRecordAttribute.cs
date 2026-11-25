namespace Magic.IndexedDb.SchemaAnnotations;

/// <summary>Requires record encryption and names the CLR properties allowed to remain public.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class MagicEncryptRecordAttribute(params string[] exposedProperties) : Attribute
{
    public IReadOnlyList<string> ExposedProperties { get; } = Array.AsReadOnly(exposedProperties);
}
