using Magic.IndexedDb.SchemaAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Helpers
{
    public static class SchemaHelper
    {
        public const string defaultNone = "DefaultedNone";

        public static string GetSchemaName<T>() where T : class
        {
            Type type = typeof(T);
            string schemaName;
            var schemaAttribute = type.GetCustomAttribute<MagicTableAttribute>();
            if (schemaAttribute != null)
            {
                schemaName = schemaAttribute.SchemaName;
            }
            else
            {
                schemaName = type.Name;
            }
            return schemaName;
        }

        public static List<string> GetPropertyNamesFromExpression<T>(Expression<Func<T, bool>> predicate) where T : class
        {
            var propertyNames = new List<string>();

            if (predicate.Body is BinaryExpression binaryExpression)
            {
                var left = binaryExpression.Left as MemberExpression;
                var right = binaryExpression.Right as MemberExpression;

                if (left != null) propertyNames.Add(left.Member.Name);
                if (right != null) propertyNames.Add(right.Member.Name);
            }
            else if (predicate.Body is MethodCallExpression methodCallExpression)
            {
                var argument = methodCallExpression.Object as MemberExpression;
                if (argument != null) propertyNames.Add(argument.Member.Name);
            }

            return propertyNames;
        }


        public static List<StoreSchema> GetAllSchemas(string databaseName = null)
        {
            List<StoreSchema> schemas = new List<StoreSchema>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                var types = assembly.GetTypes();

                foreach (var type in types)
                {
                    //var attributes = type.GetCustomAttributes(typeof(SchemaAnnotationDbAttribute), false);

                    //if (attributes.Length > 0)
                    //{
                    var schemaAttribute = type.GetCustomAttribute<MagicTableAttribute>();
                    if (schemaAttribute != null)
                    {
                        string DbName = null;

                        if (!String.IsNullOrWhiteSpace(databaseName))
                            DbName = databaseName;
                        else
                            DbName = defaultNone;

                        if (schemaAttribute.DatabaseName.Equals(DbName))
                        {
                            //var schema = typeof(SchemaHelper).GetMethod("GetStoreSchema").MakeGenericMethod(type).Invoke(null, new object[] { }) as StoreSchema;
                            var schema = GetStoreSchema(type);
                            schemas.Add(schema);
                        }
                    }
                }
            }

            return schemas;
        }

        public static StoreSchema GetStoreSchema<T>(string name = null, bool PrimaryKeyAuto = true) where T : class
        {
            Type type = typeof(T);
            return GetStoreSchema(type, name, PrimaryKeyAuto);
        }

        public static StoreSchema GetStoreSchema(Type type, string name = null, bool PrimaryKeyAuto = true)
        {
            StoreSchema schema = new StoreSchema();
            schema.PrimaryKeyAuto = PrimaryKeyAuto;


            //if (String.IsNullOrWhiteSpace(name))
            //    schema.Name = type.Name;
            //else
            //    schema.Name = name;

            // Get the schema name from the SchemaAnnotationDbAttribute if it exists
            var schemaAttribute = type.GetCustomAttribute<MagicTableAttribute>();
            if (schemaAttribute != null)
            {
                schema.Name = schemaAttribute.SchemaName;
            }
            else if (!String.IsNullOrWhiteSpace(name))
            {
                schema.Name = name;
            }
            else
            {
                schema.Name = type.Name;
            }

            // Get the primary key property
            PropertyInfo? primaryKeyProperty = type.GetProperties().FirstOrDefault(prop => Attribute.IsDefined(prop, typeof(MagicPrimaryKeyAttribute)));

            if (primaryKeyProperty == null)
            {
                throw new InvalidOperationException("The entity does not have a primary key attribute.");
            }

            if (type.GetProperties().Count(prop => Attribute.IsDefined(prop, typeof(MagicPrimaryKeyAttribute))) > 1)
            {
                throw new InvalidOperationException("The entity has more than one primary key attribute.");
            }

            schema.PrimaryKey = primaryKeyProperty.GetPropertyColumnName<MagicPrimaryKeyAttribute>();

            // Get the unique index properties
            var uniqueIndexProperties = type.GetProperties().Where(prop => Attribute.IsDefined(prop, typeof(MagicUniqueIndexAttribute)));

            foreach (PropertyInfo? prop in uniqueIndexProperties)
            {
                if (prop != null)
                {
                    schema.UniqueIndexes.Add(prop.GetPropertyColumnName<MagicUniqueIndexAttribute>());
                }
            }

            // Get the index properties
            var indexProperties = type.GetProperties().Where(prop => Attribute.IsDefined(prop, typeof(MagicIndexAttribute)));

            foreach (var prop in indexProperties)
            {
                schema.Indexes.Add(prop.GetPropertyColumnName<MagicIndexAttribute>());
            }

            return schema;
        }

        public static string GetPropertyColumnName(this PropertyInfo prop, Type attributeType)
        {
            Attribute? attribute = Attribute.GetCustomAttribute(prop, attributeType);

            string columnName = prop.Name;

            if (attribute != null)
            {
                PropertyInfo[] properties = attributeType.GetProperties();
                foreach (PropertyInfo property in properties)
                {
                    if (Attribute.IsDefined(property, typeof(MagicColumnNameDesignatorAttribute)))
                    {
                        object? designatedColumnNameObject = property.GetValue(attribute);
                        if (designatedColumnNameObject != null)
                        {
                            string designatedColumnName = designatedColumnNameObject as string ?? designatedColumnNameObject.ToString();
                            if (!string.IsNullOrWhiteSpace(designatedColumnName))
                            {
                                columnName = designatedColumnName;
                            }
                        }
                        break;
                    }
                }
            }

            return columnName;
        }
        public static string GetPropertyColumnName<T>(this PropertyInfo prop) where T : Attribute
        {
            T? attribute = (T?)Attribute.GetCustomAttribute(prop, typeof(T));

            return prop.GetPropertyColumnName(typeof(T));

        }


        //public StoreSchema GetStoreSchema<T>(bool PrimaryKeyAuto = true) where T : class
        //{
        //    StoreSchema schema = new StoreSchema();
        //    schema.PrimaryKeyAuto = PrimaryKeyAuto;


        //    return schema;
        //}
    }
}
