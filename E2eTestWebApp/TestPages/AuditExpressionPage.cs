using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Magic.IndexedDb;
using Magic.IndexedDb.SchemaAnnotations;
using Microsoft.AspNetCore.Components;
using TestBase.Repository;

namespace E2eTestWebApp.TestPages;

[Route("/AuditExpressions")]
public class AuditExpressionPage(IMagicIndexedDb magic) : TestPageBase
{
    private static readonly DateTime Day = new(2030, 4, 5, 0, 0, 0, DateTimeKind.Utc);
    private static AuditExpressionRecord[] Rows() =>
    [
        new() { Id = 1, Name = "ABC", Number = 1, NullableNumber = null, When = Day, Status = AuditExpressionStatus.Active },
        new() { Id = 2, Name = "abc", Number = 257, NullableNumber = 9, When = Day.AddHours(12), Status = AuditExpressionStatus.Inactive },
        new() { Id = 3, Name = "other", Number = 1, NullableNumber = 10, When = Day.AddDays(1), Status = AuditExpressionStatus.Active },
        new() { Id = 4, Name = null!, Number = 2, NullableNumber = 11, When = Day.AddDays(-1), Status = AuditExpressionStatus.Inactive },
        new() { Id = 5, Name = "last", Number = 0, NullableNumber = 0, When = DateTime.MaxValue, Status = AuditExpressionStatus.Inactive }
    ];

    public Task<string> CapturedOperands()
    {
        var value = 257;
        var comparison = StringComparison.OrdinalIgnoreCase;
        return CheckPredicates(
            row => row.Number == (byte)value,
            row => row.Name != null && row.Name.Contains("abc", comparison));
    }

    public Task<string> NullablePredicates()
    {
        string?[] any = [null, "ABC"];
        string?[] all = [null];
        int?[] membership = [null, 10];
        return CheckPredicates(
            row => !(row.NullableNumber > 10),
            row => !(row.NullableNumber >= 10),
            row => !(row.NullableNumber < 10),
            row => !(row.NullableNumber <= 10),
            row => any.Any(value => row.Name == value),
            row => all.All(value => row.Name == value),
            row => membership.Contains(row.NullableNumber));
    }

    public Task<string> DateCutoffs()
    {
        var noon = Day.AddHours(12);
        var last = DateTime.MaxValue.Date;
        return CheckPredicates(
            row => row.When.Date == noon,
            row => row.When.Date != noon,
            row => row.When.Date > noon,
            row => row.When.Date >= noon,
            row => row.When.Date < noon,
            row => row.When.Date <= noon,
            row => row.When.Date == last,
            row => row.When.Date <= last,
            row => row.When.Date > last);
    }

    public Task<string> ConvertedPropertyOperands()
    {
        AuditExpressionStatus[] values = [AuditExpressionStatus.Active];
        return CheckPredicates(
            row => row.Status == AuditExpressionStatus.Active,
            row => row.Status != AuditExpressionStatus.Active,
            row => values.Contains(row.Status));
    }

    private async Task<string> CheckPredicates(params Expression<Func<AuditExpressionRecord, bool>>[] predicates)
    {
        var rows = Rows();
        var query = await magic.Query<AuditExpressionRecord>();
        await query.ClearTable();
        await query.AddRangeAsync(rows);
        foreach (var predicate in predicates)
        {
            var expected = rows.Where(predicate.Compile()).Select(row => row.Id).Order().ToArray();
            var indexed = (await query.Where(predicate).ToListAsync()).Select(row => row.Id).Order().ToArray();
            var cursor = (await query.Cursor(predicate).ToListAsync()).Select(row => row.Id).Order().ToArray();
            if (!expected.SequenceEqual(indexed) || !expected.SequenceEqual(cursor))
                return $"{predicate}: expected [{string.Join(',', expected)}], indexed [{string.Join(',', indexed)}], cursor [{string.Join(',', cursor)}]";
        }
        return "OK";
    }
}

public enum AuditExpressionStatus { Inactive, Active }

public sealed class AuditExpressionRecord : MagicTableTool<AuditExpressionRecord>, IMagicTable<IndexDbContext>
{
    public int Id { get; set; }
    [MagicIndex]
    public string Name { get; set; } = "";
    [MagicIndex]
    public int Number { get; set; }
    [MagicIndex]
    public int? NullableNumber { get; set; }
    [MagicIndex]
    public DateTime When { get; set; }
    [MagicIndex, JsonConverter(typeof(JsonStringEnumConverter<AuditExpressionStatus>))]
    public AuditExpressionStatus Status { get; set; }

    public IndexDbContext Databases { get; } = new();
    public IndexedDbSet GetDefaultDatabase() => IndexDbContext.Client;
    public string GetTableName() => nameof(AuditExpressionRecord);
    public IMagicCompoundKey GetKeys() => CreatePrimaryKey(row => row.Id, autoIncrement: false);
    public List<IMagicCompoundIndex> GetCompoundIndexes() => [];
}
