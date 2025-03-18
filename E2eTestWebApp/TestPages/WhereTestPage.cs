using Magic.IndexedDb;
using Magic.IndexedDb.Helpers;
using Microsoft.AspNetCore.Components;
using System.Text.Json;

namespace E2eTestWebApp.TestPages;

[Route("/WhereTest")]
public class WhereTestPage(IMagicIndexedDb magic) : TestPageBase
{
    [MagicTable("Records", null)]
    private class Record
    {
        [MagicPrimaryKey("Id")]
        public int Id { get; set; }

        public int Int32Field { get; set; }
    }

    public async Task<string> Where1()
    {
        var database = await magic.OpenAsync(new DbStore()
        {
            Name = "Where.Where1",
            Version = 1,
            StoreSchemas = [SchemaHelper.GetStoreSchema(typeof(Record))]
        });
        await database.AddAsync<Record, int>(new Record()
        {
            Id = 1,
            Int32Field = 1
        });
        await database.AddAsync<Record, int>(new Record()
        {
            Id = 2,
            Int32Field = 2
        });
        await database.AddAsync<Record, int>(new Record()
        {
            Id = 3,
            Int32Field = 3
        });
        var result = await database
            .Where<Record>(x => x.Int32Field < 3)
            .ToListAsync();
        return JsonSerializer.Serialize(result.Select(x => x.Id));
    }
}