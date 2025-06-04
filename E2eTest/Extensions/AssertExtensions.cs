using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

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

    public static void AreJsonEqual(this Assert _, string expected, string actual)
    {
        var element1 = JsonSerializer.Deserialize<JsonElement>(expected);
        var element2 = JsonSerializer.Deserialize<JsonElement>(actual);
        Assert.AreEqual(element1, element2, JsonElementDeepComparer.Instance);
    }
}
