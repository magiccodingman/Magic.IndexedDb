using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb.Interfaces
{
    public interface ITypedArgument
    {
        string Serialize(); // Forces all TypedArgument<T> instances to implement serialization
    }
}
