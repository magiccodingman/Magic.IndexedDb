using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Magic.IndexedDb.Interfaces;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed class PublicApiContractTests
{
    [TestMethod]
    public void TypedArgumentSerializationMembers_RemainPublic()
    {
        var methods = typeof(ITypedArgument).GetMethods().Select(method => method.Name).ToHashSet();

        CollectionAssert.IsSubsetOf(
            new[] { "Serialize", "SerializeToJsonElement", "SerializeToJsonString" },
            methods.ToArray());
    }

    [TestMethod]
    public void PublicApi_MatchesReviewedBaseline()
    {
        var baselinePath = GetBaselinePath();
        var actual = BuildPublicApiSnapshot();

        if (Environment.GetEnvironmentVariable("UPDATE_PUBLIC_API_BASELINE") == "1")
        {
            File.WriteAllText(baselinePath, actual + Environment.NewLine);
            return;
        }

        var expected = File.ReadAllText(baselinePath).TrimEnd();
        if (string.Equals(expected, actual, StringComparison.Ordinal))
            return;

        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var difference = Enumerable.Range(0, Math.Max(expectedLines.Length, actualLines.Length))
            .First(index => index >= expectedLines.Length ||
                            index >= actualLines.Length ||
                            expectedLines[index] != actualLines[index]);

        Assert.Fail(
            $"The public API changed at line {difference + 1}.{Environment.NewLine}" +
            $"Expected: {(difference < expectedLines.Length ? expectedLines[difference] : "<end>")}{Environment.NewLine}" +
            $"Actual:   {(difference < actualLines.Length ? actualLines[difference] : "<end>")}{Environment.NewLine}" +
            "Review the compatibility impact, then regenerate intentionally with " +
            "UPDATE_PUBLIC_API_BASELINE=1 dotnet test --filter PublicApi_MatchesReviewedBaseline.");
    }

    private static string GetBaselinePath([CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, "PublicApiBaseline.txt");

    private static string BuildPublicApiSnapshot()
    {
        var lines = new List<string>();
        foreach (var type in typeof(IMagicIndexedDb).Assembly.GetExportedTypes()
                     .OrderBy(type => TypeName(type), StringComparer.Ordinal))
        {
            lines.Add(DescribeType(type));

            var members = new List<string>();
            members.AddRange(type.GetConstructors(DeclaredPublic)
                .Select(constructor => $"  ctor {TypeName(type)}({Parameters(constructor.GetParameters())})"));
            members.AddRange(type.GetFields(DeclaredPublic)
                .Select(field => $"  field {(field.IsStatic ? "static " : "")}" +
                                 $"{(field.IsInitOnly ? "readonly " : "")}{TypeName(field.FieldType)} {field.Name}" +
                                 (field.IsLiteral ? $" = {FormatValue(field.GetRawConstantValue())}" : string.Empty)));
            members.AddRange(type.GetProperties(DeclaredPublic)
                .Select(property =>
                {
                    var index = property.GetIndexParameters();
                    var name = index.Length == 0
                        ? property.Name
                        : $"this[{Parameters(index)}]";
                    var accessors = string.Join(" ", new[]
                    {
                        property.GetMethod?.IsPublic == true ? "get;" : null,
                        property.SetMethod?.IsPublic == true ? "set;" : null
                    }.Where(value => value is not null));
                    return $"  property {TypeName(property.PropertyType)} {name} {{ {accessors} }}";
                }));
            members.AddRange(type.GetEvents(DeclaredPublic)
                .Select(@event => $"  event {TypeName(@event.EventHandlerType!)} {@event.Name}"));
            members.AddRange(type.GetMethods(DeclaredPublic)
                .Where(method => !method.IsSpecialName)
                .Select(method =>
                {
                    var generic = method.IsGenericMethodDefinition
                        ? $"<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>"
                        : string.Empty;
                    return $"  method {(method.IsStatic ? "static " : string.Empty)}" +
                           $"{TypeName(method.ReturnType)} {method.Name}{generic}({Parameters(method.GetParameters())})";
                }));

            lines.AddRange(members.OrderBy(member => member, StringComparer.Ordinal));
        }

        return string.Join('\n', lines);
    }

    private const BindingFlags DeclaredPublic =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static string DescribeType(Type type)
    {
        var kind = type.IsInterface ? "interface" :
            type.IsEnum ? "enum" :
            type.IsValueType ? "struct" :
            type.IsAbstract && type.IsSealed ? "static class" :
            "class";
        var bases = new List<string>();
        if (type.BaseType is not null && type.BaseType != typeof(object) && !type.IsEnum && !type.IsValueType)
            bases.Add(TypeName(type.BaseType));
        bases.AddRange(type.GetInterfaces().Select(TypeName));
        return $"{kind} {TypeName(type)}" +
               (bases.Count == 0
                   ? string.Empty
                   : $" : {string.Join(", ", bases.Distinct().Order(StringComparer.Ordinal))}");
    }

    private static string Parameters(IEnumerable<ParameterInfo> parameters) =>
        string.Join(", ", parameters.Select(parameter =>
        {
            var modifier = parameter.IsOut ? "out " :
                parameter.ParameterType.IsByRef ? "ref " : string.Empty;
            var optional = parameter.HasDefaultValue
                ? $" = {FormatValue(parameter.DefaultValue)}"
                : string.Empty;
            return $"{modifier}{TypeName(parameter.ParameterType)} {parameter.Name}{optional}";
        }));

    private static string TypeName(Type type)
    {
        if (type.IsByRef)
            return TypeName(type.GetElementType()!);
        if (type.IsArray)
            return $"{TypeName(type.GetElementType()!)}[]";
        if (type.IsGenericParameter)
            return type.Name;
        if (!type.IsGenericType)
            return type.FullName?.Replace('+', '.') ?? type.Name;

        var definitionName = (type.GetGenericTypeDefinition().FullName ?? type.Name)
            .Split('`')[0]
            .Replace('+', '.');
        return $"{definitionName}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        char character => $"'{character}'",
        bool boolean => boolean ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null"
    };
}
