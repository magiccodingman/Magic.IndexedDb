using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Models
{
    public class MagicJsonSerializationSettings
    {
        private JsonSerializerOptions _options = new();

        public JsonSerializerOptions Options
        {
            get => _options;
            set => _options = value ?? new JsonSerializerOptions(); // Ensure it never gets null
        }

        public bool UseCamelCase
        {
            get => _options.PropertyNamingPolicy == JsonNamingPolicy.CamelCase;
            set
            {
                _options = new JsonSerializerOptions(_options) // Clone existing settings
                {
                    PropertyNamingPolicy = value ? JsonNamingPolicy.CamelCase : null
                };
            }
        }
    }
}
