using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models
{
    public class CustomContractResolver : DefaultContractResolver
    {
        private readonly Dictionary<string, string> _propertyMappings;

        public CustomContractResolver(Dictionary<string, string> propertyMappings)
        {
            _propertyMappings = propertyMappings;
        }

        protected override string ResolvePropertyName(string propertyName)
        {
            if (_propertyMappings.TryGetValue(propertyName, out var resolvedName))
            {
                return resolvedName;
            }

            return base.ResolvePropertyName(propertyName);
        }
    }
}
