﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Magic.IndexedDb.Interfaces;
using Magic.IndexedDb.SchemaAnnotations;

namespace Magic.IndexedDb.SchemaAnnotations;

/// <summary>
/// Indexes this key
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class MagicIndexAttribute : Attribute, IColumnNamed
{
    public string ColumnName { get; }

    public MagicIndexAttribute(string columnName = null)
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