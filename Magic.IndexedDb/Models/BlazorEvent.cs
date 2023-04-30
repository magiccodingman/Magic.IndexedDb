using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    public class BlazorDbEvent
    {
        public Guid Transaction { get; set; }
        public bool Failed { get; set; }
        public string Message { get; set; }
    }
}
