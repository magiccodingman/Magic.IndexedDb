using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models
{
    public struct MagicPropertyEntry
    {
        /// <summary>
        /// Constructor for initializing MagicPropertyEntry while reducing memory footprint.
        /// </summary>
        public MagicPropertyEntry(PropertyInfo property, IColumnNamed? columnNamedAttribute,
                                  bool indexed, bool uniqueIndex, bool primaryKey, bool notMapped)
        {
            Property = property;
            _columnNamedAttribute = columnNamedAttribute;
            Indexed = indexed;
            UniqueIndex = uniqueIndex;
            PrimaryKey = primaryKey;
            NotMapped = notMapped;

            IsComplexType = PropertyMappingCache.IsComplexType(property.PropertyType);

            // 🔥 Identify if this property is a constructor parameter
            var declaringType = property.DeclaringType!;
            var constructor = declaringType.GetConstructors().FirstOrDefault();

            // 🔥 Precompute and cache the default value
            DefaultValue = property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null;

            // 🔥 Create delegates for performance 🔥
            if (property.CanRead)
            {
                Getter = CreateGetter(property);
            }
            else
            {
                Getter = _ => null; // No getter available
            }

            if (property.CanWrite)
            {
                Setter = CreateSetter(property);
            }
            else
            {
                Setter = (_, __) => { }; // No setter available
            }
        }

        public object? DefaultValue { get; }

        /// <summary>
        /// If any Magic attribute was placed on a property. We never 
        /// camel case if that's the current json setting. These must stay 
        /// as they were initially designated.
        /// </summary>
        public bool NeverCamelCase
        {
            get
            {
                if (PrimaryKey || UniqueIndex || Indexed)
                    return true;
                else
                    return false;
            }
        }

        public bool IsComplexType { get; }

        /// 🔥 **Precomputed Getter Delegate** 🔥
        public Func<object, object?> Getter { get; }

        /// 🔥 **Precomputed Setter Delegate** 🔥
        public Action<object, object?> Setter { get; }

        private static Func<object, object?> CreateGetter(PropertyInfo property)
        {
            var method = property.GetGetMethod(nonPublic: true);
            if (method == null) return _ => null;

            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var castInstance = Expression.Convert(instanceParam, property.DeclaringType!);
            var propertyAccess = Expression.Property(castInstance, property);
            var castResult = Expression.Convert(propertyAccess, typeof(object));

            return Expression.Lambda<Func<object, object?>>(castResult, instanceParam).Compile();
        }

        private static Action<object, object?> CreateSetter(PropertyInfo property)
        {
            var method = property.GetSetMethod(nonPublic: true);
            if (method == null) return (_, __) => { };

            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var valueParam = Expression.Parameter(typeof(object), "value");

            var castInstance = Expression.Convert(instanceParam, property.DeclaringType!);
            var castValue = Expression.Convert(valueParam, property.PropertyType);

            var propertySetter = Expression.Call(castInstance, method, castValue);

            return Expression.Lambda<Action<object, object?>>(propertySetter, instanceParam, valueParam).Compile();
        }


        /// <summary>
        /// Reference to the IColumnNamed attribute if present, otherwise null. 
        /// This prevents saving the original string provided unecessary which 
        /// saves minimum 20 bytes if the IColumnName originally was empty. 
        /// Aka it means we're saving much more than 20 bytes per item.
        /// </summary>
        private readonly IColumnNamed? _columnNamedAttribute;

        /// <summary>
        /// The JavaScript/Column Name mapping
        /// </summary>
        public string JsPropertyName =>
            _columnNamedAttribute?.ColumnName ?? Property.Name;

        /// <summary>
        /// Reference to the PropertyInfo instead of storing the C# name as a string. 
        /// Which reduces memory print from the minimum empty string size of 20 bytes 
        /// to now only 8 bytes (within 64 bit systems).
        /// </summary>
        public PropertyInfo Property { get; set; }

        /// <summary>
        /// The C# Property Name mapping
        /// </summary>
        public string CsharpPropertyName => Property.Name;

        /// <summary>
        /// Property with the, "MagicIndexAttribute" attribute appended
        /// </summary>
        public bool Indexed { get; set; }

        /// <summary>
        /// Property with the, "MagicUniqueIndexAttribute" attribute appended
        /// </summary>
        public bool UniqueIndex { get; set; }

        /// <summary>
        /// Property with the, "MagicPrimaryKeyAttribute" attribute appended
        /// </summary>
        public bool PrimaryKey { get; set; }

        /// <summary>
        /// Property with the, "MagicNotMappedAttribute" attribute appended
        /// </summary>
        public bool NotMapped { get; set; }
    }
}
