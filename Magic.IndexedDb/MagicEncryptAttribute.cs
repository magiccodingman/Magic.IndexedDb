using System.Reflection;

namespace Magic.IndexedDb;

[Obsolete("Future refactor is planned for significantly improved encryption system. This feature is no longer supported nor working.")]
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class MagicEncryptAttribute : Attribute
{
    public MagicEncryptAttribute()
    {
    }

    public void Validate(PropertyInfo property)
    {
        if (property.PropertyType != typeof(string))
        {
            throw new ArgumentException("EncryptDbAttribute can only be used on string properties");
        }
    }
}