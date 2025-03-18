using Magic.IndexedDb;
using Magic.IndexedDb.Helpers;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Magic.IndexedDb.SchemaAnnotations;
using System.Text.Json;

namespace E2eTestWebApp.TestPages;

[Route("/SingleRecordBasicTest")]
public class SingleRecordBasicTestPage(IMagicIndexedDb magic) : TestPageBase
{
    private class NestedItem
    {
        public int Value { get; set; }
    }

    [MagicTable("Records", null)]
    private class Record
    {
        [MagicPrimaryKey("id")]
        public int Id { get; set; }

        public string? Normal { get; set; }

        [MagicNotMapped]
        public bool Ignored { get; set; }

        [MagicIndex("Renamed")]
        public char ShouldBeRenamed { get; set; }

        [MagicIndex]
        public string? Index { get; set; }

        [MagicIndex]
        public Guid UniqueIndex { get; set; }

        public DayOfWeek Enum { get; set; }

        public NestedItem? Nested { get; set; }

        public long LargeNumber { get; set; }
    }
    static Record NewSample => new Record()
    {
        Id = 12,
        Normal = "Norm",
        Ignored = true,
        ShouldBeRenamed = 'R',
        Index = "I",
        UniqueIndex = Guid.Parse("633A97D2-0C92-4C68-883B-364F94AD6030"),
        Enum = DayOfWeek.Sunday,
        Nested = new() { Value = 1234 },
        LargeNumber = 9007199254740991
    };

    public async Task<string> Add()
    {
        var database = await magic.OpenAsync(new DbStore()
        {
            Name = "SingleRecordBasic.Add",
            Version = 1,
            StoreSchemas = [SchemaHelper.GetStoreSchema(typeof(Record))]
        });
        var id = await database.AddAsync<Record, int>(NewSample);
        return id.ToString();
    }

    public async Task<string> Delete()
    {
        var database = await magic.OpenAsync(new DbStore()
        {
            Name = "SingleRecordBasic.Delete",
            Version = 1,
            StoreSchemas = [SchemaHelper.GetStoreSchema(typeof(Record))]
        });
        _ = await database.AddAsync<Record, int>(NewSample);
        await database.DeleteAsync(NewSample);
        return "OK";
    }

    public async Task<string> Update()
    {
        var database = await magic.OpenAsync(new DbStore()
        {
            Name = "SingleRecordBasic.Update",
            Version = 1,
            StoreSchemas = [SchemaHelper.GetStoreSchema(typeof(Record))]
        });
        _ = await database.AddAsync<Record, int>(NewSample);

        var updated = NewSample;
        updated.Normal = "Updated";
        var count = await database.UpdateAsync(updated);
        return count.ToString();
    }

    public async Task<string> GetById()
    {
        var database = await magic.OpenAsync(new DbStore()
        {
            Name = "SingleRecordBasic.GetById",
            Version = 1,
            StoreSchemas = [SchemaHelper.GetStoreSchema(typeof(Record))]
        });
        var id = await database.AddAsync<Record, int>(NewSample);
        var result = await database.GetByIdAsync<Record>(id);
        return result.Normal;
    }

    public async Task<string> GetAll()
    {
        var database = await magic.OpenAsync(new DbStore()
        {
            Name = "SingleRecordBasic.GetAll",
            Version = 1,
            StoreSchemas = [SchemaHelper.GetStoreSchema(typeof(Record))]
        });
        _ = await database.AddAsync<Record, int>(NewSample);
        var result = await database.GetAllAsync<Record>();
        return JsonSerializer.Serialize(result);
    }
}