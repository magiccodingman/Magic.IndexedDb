using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Testing.Models
{
    public class TestResponse
    {
        public bool Success { get; set; } = false;
        public string? Message { get; set; }
    }
}
