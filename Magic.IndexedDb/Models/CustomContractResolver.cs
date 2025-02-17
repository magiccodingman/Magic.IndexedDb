using Newtonsoft.Json.Serialization;

namespace Magic.IndexedDb.Models
{
    public class CustomContractResolver : DefaultContractResolver
    {
        private readonly Dictionary<string, string> _propertyMappings;

        public CustomContractResolver(Dictionary<string, string> propertyMappings)
        {
            this._propertyMappings = propertyMappings;
        }

        protected override string ResolvePropertyName(string propertyName)
        {
            if (this._propertyMappings.TryGetValue(propertyName, out var resolvedName))
            {
                return resolvedName;
            }

            return base.ResolvePropertyName(propertyName);
        }
    }
}
