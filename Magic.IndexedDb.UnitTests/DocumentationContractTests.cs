using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Magic.IndexedDb.LinqTranslation.Interfaces;

namespace Magic.IndexedDb.UnitTests;

[TestClass]
public sealed partial class DocumentationContractTests
{
    [TestMethod]
    public void RelativeMarkdownLinks_Resolve()
    {
        var repositoryRoot = GetRepositoryRoot();
        var markdownFiles = Directory
            .EnumerateFiles(repositoryRoot, "*.md", SearchOption.AllDirectories)
            .Where(path => !IsIgnoredPath(repositoryRoot, path))
            .ToArray();
        var brokenLinks = new List<string>();

        foreach (var markdownFile in markdownFiles)
        {
            var markdown = File.ReadAllText(markdownFile);
            foreach (Match match in MarkdownLink().Matches(markdown))
            {
                var target = NormalizeLinkTarget(match.Groups["target"].Value);
                if (ShouldIgnoreTarget(target))
                    continue;

                var pathOnly = target.Split(['#', '?'], 2)[0];
                if (string.IsNullOrWhiteSpace(pathOnly))
                    continue;

                var resolved = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(markdownFile)!, Uri.UnescapeDataString(pathOnly)));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    brokenLinks.Add(
                        $"{Path.GetRelativePath(repositoryRoot, markdownFile)} -> {target}");
                }
            }
        }

        Assert.AreEqual(
            0,
            brokenLinks.Count,
            "Broken relative documentation links:" + Environment.NewLine +
            string.Join(Environment.NewLine, brokenLinks));
    }

    [TestMethod]
    public void DocumentationIndex_LinksEveryCurrentPage()
    {
        var repositoryRoot = GetRepositoryRoot();
        var docsRoot = Path.Combine(repositoryRoot, "docs");
        var indexPath = Path.Combine(docsRoot, "README.md");
        var index = File.ReadAllText(indexPath);
        var missingPages = Directory
            .EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, indexPath, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(docsRoot, path).Replace('\\', '/'))
            .Where(relativePath => !index.Contains($"({relativePath})", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.AreEqual(
            0,
            missingPages.Length,
            "docs/README.md does not link these pages:" + Environment.NewLine +
            string.Join(Environment.NewLine, missingPages));
    }

    [TestMethod]
    public void SupportedConsumerSurface_HasCanonicalReferenceCoverage()
    {
        var repositoryRoot = GetRepositoryRoot();
        var referenceRoot = Path.Combine(repositoryRoot, "docs", "reference");
        var referenceCorpus = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(referenceRoot, "*.md", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Type[] supportedInterfaces =
        [
            typeof(IMagicIndexedDb),
            typeof(IMagicExecute<>),
            typeof(IMagicQuery<>),
            typeof(IMagicQueryStaging<>),
            typeof(IMagicQueryOrderable<>),
            typeof(IMagicQueryOrderableTable<>),
            typeof(IMagicQueryPaginationTake<>),
            typeof(IMagicQueryFinal<>),
            typeof(IMagicCursor<>),
            typeof(IMagicCursorStage<>),
            typeof(IMagicCursorPaginationTake<>),
            typeof(IMagicCursorSkip<>),
            typeof(IMagicCursorFinal<>),
            typeof(IMagicDatabaseScoped)
        ];

        var missingTerms = new List<string>();
        foreach (var type in supportedInterfaces)
        {
            var typeName = type.Name.Split('`')[0];
            if (!referenceCorpus.Contains(typeName, StringComparison.Ordinal))
                missingTerms.Add(typeName);

            var members = type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(member => member.MemberType is MemberTypes.Method or MemberTypes.Property)
                .Where(member => member is not MethodInfo method || !method.IsSpecialName)
                .Select(member => member.Name)
                .Distinct(StringComparer.Ordinal);
            missingTerms.AddRange(
                members.Where(member => !referenceCorpus.Contains(member, StringComparison.Ordinal)));
        }

        string[] additionalContractTerms =
        [
            "AddMagicBlazorDB",
            "BlazorInteropMode",
            "QuotaUsage",
            "IMagicRepository",
            "IMagicTableBase",
            "IMagicTable<TDbSets>",
            "IndexedDbSet",
            "MagicTableTool<T>",
            "MagicConstructor",
            "MagicIndex",
            "MagicUniqueIndex",
            "MagicName",
            "MagicNotMapped"
        ];
        missingTerms.AddRange(
            additionalContractTerms.Where(term => !referenceCorpus.Contains(term, StringComparison.Ordinal)));

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            missingTerms.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            "Supported consumer API terms missing from docs/reference.");
    }

    private static string GetRepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Directory.GetParent(Path.GetDirectoryName(sourceFile)!)!.FullName;

    private static bool IsIgnoredPath(string repositoryRoot, string path)
    {
        var relative = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
        return relative.StartsWith(".git/", StringComparison.Ordinal) ||
               relative.Contains("/bin/", StringComparison.Ordinal) ||
               relative.Contains("/obj/", StringComparison.Ordinal) ||
               relative.Contains("/TestResults/", StringComparison.Ordinal);
    }

    private static string NormalizeLinkTarget(string target)
    {
        target = target.Trim();
        if (target.StartsWith('<') && target.EndsWith('>'))
            return target[1..^1];

        var whitespace = target.IndexOfAny([' ', '\t', '\r', '\n']);
        return whitespace < 0 ? target : target[..whitespace];
    }

    private static bool ShouldIgnoreTarget(string target) =>
        string.IsNullOrWhiteSpace(target) ||
        target.StartsWith('#') ||
        target.StartsWith('/') ||
        Uri.TryCreate(target, UriKind.Absolute, out _);

    [GeneratedRegex(@"!?\[[^\]]*\]\((?<target>[^)]+)\)")]
    private static partial Regex MarkdownLink();
}
