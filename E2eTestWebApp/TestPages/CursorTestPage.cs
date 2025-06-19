using Magic.IndexedDb;
using Magic.IndexedDb.Helpers;
using Microsoft.AspNetCore.Components;
using System.Text.Json;
using E2eTestWebApp.Models;

namespace E2eTestWebApp.TestPages;

[Route("/CursorTest")]
public class CursorTestPage(IMagicIndexedDb magic) : TestPageBase
{
    public async Task<string> Where1()
    {
        var db = await magic.Query<Person>();

        return "Incorrect";
    }
}