using Magic.IndexedDb;
using Magic.IndexedDb.Helpers;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Magic.IndexedDb.SchemaAnnotations;

namespace E2eTestWebApp.TestPages;

[Route("/SingleRecordBasicTest")]
public class SingleRecordBasicTestPage(IMagicDbFactory magic) : TestPageBase
{
    private record NestedItem(int Value);

    [MagicTable("Records", null)]
    private record Record(
        [property: MagicPrimaryKey] 
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] 
        int Id,

        string Normal,

        [property: JsonIgnore] 
        bool Ignored,

        [property: JsonPropertyName("Renamed")] 
        char ShouldBeRenamed,

        [property: MagicIndex]
        string Index,

        [property: MagicIndex]
        Guid UniqueIndex,

        DayOfWeek Enum,

        NestedItem Nested,

        long LargeNumber)
    {
        public static Record Sample => new Record(
            Id: 12,
            Normal: "Norm",
            Ignored: true,
            ShouldBeRenamed: 'R',
            Index: "I",
            UniqueIndex: Guid.Parse("633A97D2-0C92-4C68-883B-364F94AD6030"),
            Enum: DayOfWeek.Sunday,
            Nested: new(1234),
            LargeNumber: 9007199254740991);
    }

    public async Task<string> Add()
    {
        var database = await magic.OpenAsync(new DbStore()
        {
            Name = "SingleRecordBasic.Add",
            Version = 1,
            StoreSchemas = [SchemaHelper.GetStoreSchema<Record>(null, false)]
        });
        var id = await database.AddAsync<Record, int>(Record.Sample);
        return id.ToString();
    }

    public async Task<string> Delete()
    {
        var database = await magic.OpenAsync(new DbStore()
        {
            Name = "SingleRecordBasic.Delete",
            Version = 1,
            StoreSchemas = [SchemaHelper.GetStoreSchema<Record>(null, false)]
        });
        _ = await database.AddAsync<Record, int>(Record.Sample);
        await database.DeleteAsync(Record.Sample);
        return "OK";
    }

    public async Task<string> Update()
    {
        var database = await magic.OpenAsync(new DbStore()
        {
            Name = "SingleRecordBasic.Update",
            Version = 1,
            StoreSchemas = [SchemaHelper.GetStoreSchema<Record>(null, false)]
        });
        _ = await database.AddAsync<Record, int>(Record.Sample);
        var count = await database.UpdateAsync(Record.Sample with { Normal = "Updated" });
        return count.ToString();
    }

    public async Task<string> GetById()
    {
        var database = await magic.OpenAsync(new DbStore()
        {
            Name = "SingleRecordBasic.GetById",
            Version = 1,
            StoreSchemas = [SchemaHelper.GetStoreSchema<Record>(null, false)]
        });
        var id = await database.AddAsync<Record, int>(Record.Sample);
        var result = await database.GetByIdAsync<Record, int>(id);
        return result.Normal;
    }
}