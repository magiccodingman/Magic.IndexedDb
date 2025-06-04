﻿using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.SchemaAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Magic.IndexedDb;

/// <summary>
/// Creates a unique key
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class MagicUniqueIndexAttribute : Attribute, IColumnNamed
{
    public string ColumnName { get; }

    public MagicUniqueIndexAttribute(string columnName = null)
    {
        if (!String.IsNullOrWhiteSpace(columnName))
        {
            ColumnName = columnName;
        }
        else
        {
            ColumnName = null;
        }
    }
}