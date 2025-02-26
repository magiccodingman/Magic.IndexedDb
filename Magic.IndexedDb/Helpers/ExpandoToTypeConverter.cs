using System.Dynamic;
using System.Linq.Expressions;

namespace Magic.IndexedDb.Helpers
{
    public static class ExpandoToTypeConverter<T>
    {
        private static readonly Dictionary<string, Action<T, object?>> PropertySetters = new();
        private static readonly Dictionary<Type, bool> NonConcreteTypeCache = new();

        private static readonly bool IsConcrete;
        private static readonly bool HasParameterlessConstructor;

        static ExpandoToTypeConverter()
        {
            Type type = typeof(T);
            IsConcrete = !(type.IsAbstract || type.IsInterface);
            HasParameterlessConstructor = type.GetConstructor(Type.EmptyTypes) != null;

            if (IsConcrete && HasParameterlessConstructor)
            {
                PrecomputePropertySetters(type);
            }
        }

        private static void PrecomputePropertySetters(Type type)
        {
            foreach (var prop in type.GetProperties().Where(p => p.CanWrite))
            {
                var targetExp = Expression.Parameter(type);
                var valueExp = Expression.Parameter(typeof(object));

                var convertedValueExp = Expression.Convert(valueExp, prop.PropertyType);

                var propertySetterExp = Expression.Lambda<Action<T, object?>>(
                    Expression.Assign(Expression.Property(targetExp, prop), convertedValueExp),
                    targetExp, valueExp
                );

                PropertySetters[prop.Name] = propertySetterExp.Compile();
            }
        }

        public static T ConvertExpando(ExpandoObject expando)
        {
            Type type = typeof(T);

            if (IsConcrete && HasParameterlessConstructor)
            {
                // Use the fastest method: Precomputed property setters
                var instance = Activator.CreateInstance<T>();
                var expandoDict = (IDictionary<string, object?>)expando;

                foreach (var kvp in expandoDict)
                {
                    if (PropertySetters.TryGetValue(kvp.Key, out var setter))
                    {
                        setter(instance, kvp.Value);
                    }
                }

                return instance;
            }
            else if (IsConcrete) // Concrete class without a parameterless constructor
            {
                var instance = Activator.CreateInstance(type);
                MagicSerializationHelper.PopulateObject(MagicSerializationHelper.SerializeObject(expando), instance);
                return (T)instance!;
            }
            else
            {
                // Last resort: If `T` is an interface or abstract class, fall back to full JSON deserialization
                var instance = MagicSerializationHelper.DeserializeObject<T>(MagicSerializationHelper.SerializeObject(expando))!;

                // Check if we can cache this as a known type
                if (!NonConcreteTypeCache.ContainsKey(type))
                {
                    NonConcreteTypeCache[type] = true;

                    // Dynamically compute property setters for this type
                    PrecomputePropertySetters(type);
                }

                return instance;
            }
        }
    }


}
