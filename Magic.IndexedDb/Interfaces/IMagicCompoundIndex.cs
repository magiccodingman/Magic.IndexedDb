﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb
{
    public interface IMagicCompoundIndex
    {
        string[] ColumnNamesInCompoundIndex { get; }
    }
}
