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
    }


}
