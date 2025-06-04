﻿using Magic.IndexedDb.Interfaces;
using System.Reflection;

namespace Magic.IndexedDb.Models;

public struct MagicPropertySearchEntry
{
    public MagicPropertySearchEntry(PropertyInfo property, IColumnNamed? columnNamedAttribute)
    {
        Property = property;
        _columnNamedAttribute = columnNamedAttribute;
    }
    /// <summary>
    /// Reference to the IColumnNamed attribute if present, otherwise null. 
    /// This prevents saving the original string provided unecessary which 
    /// saves minimum 20 bytes if the IColumnName originally was empty. 
    /// Aka it means we're saving much more than 20 bytes per item.
    /// </summary>
    public readonly IColumnNamed? _columnNamedAttribute;

    /// <summary>
    /// The JavaScript/Column Name mapping
    /// </summary>
    public string JsPropertyName =>
        _columnNamedAttribute?.ColumnName ?? Property.Name;

    /// <summary>
    /// Reference to the PropertyInfo instead of storing the C# name as a string. 
    /// Which reduces memory print from the minimum empty string size of 20 bytes 
    /// to now only 8 bytes (within 64 bit systems).
    /// </summary>
    public PropertyInfo Property { get; set; }

    /// <summary>
    /// The C# Property Name mapping
    /// </summary>
    public string CsharpPropertyName => Property.Name;
}