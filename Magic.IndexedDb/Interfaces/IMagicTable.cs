using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb;

public interface IMagicTable<TDbSets> : IMagicTableBase
{
    TDbSets Databases { get; } // Enforce that every model has a `DbSets` instance
}