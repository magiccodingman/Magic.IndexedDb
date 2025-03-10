using Magic.IndexedDb;
using Magic.IndexedDb.Helpers;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Magic.IndexedDb.SchemaAnnotations;
using System.Text.Json;
using System.Reflection.PortableExecutable;

namespace E2eTestWebApp.TestPages;

[Route("/WhereTest")]
public class WhereTestPage(IMagicDbFactory magic) : TestPageBase
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
            .Where<Record>(x => x.Int32Field < 2)
            .ToListAsync();
        return JsonSerializer.Serialize(result.Select(x => x.Id));
    }
}