using E2eTest.Entities;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace E2eTest.Extensions;
internal static class AssertExtensions
{
    private class JsonElementDeepComparer : IEqualityComparer<JsonElement>
    {
        public static JsonElementDeepComparer Instance { get; } = new();
        public bool Equals(JsonElement x, JsonElement y)
        {
            return JsonElement.DeepEquals(x, y);
        }
        public int GetHashCode([DisallowNull] JsonElement obj)
        {
            throw new NotImplementedException();
        }
    }

    public static void AreJsonEqual(this Assert assert, string expected, string actual)
    {
        var element1 = JsonSerializer.Deserialize<JsonElement>(expected);
        var element2 = JsonSerializer.Deserialize<JsonElement>(actual);
        Assert.AreEqual(element1, element2, JsonElementDeepComparer.Instance);
    }
}
