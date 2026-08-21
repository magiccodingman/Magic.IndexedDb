namespace IndexDb.Example.Pages;

public partial class Index
{
    private List<Person> allPeople { get; set; } = new List<Person>();
    private IEnumerable<Person> WhereExample { get; set; } = Enumerable.Empty<Person>();
    private double storageQuota { get; set; }
    private double storageUsage { get; set; }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                var personQuery = await _MagicDb.Query<Person>();

                await personQuery.ClearTable();

                if (!(await personQuery.ToListAsync()).Any())
                {
                    Person[] persons = new Person[] {
                        new Person { Name = "Zack", TestInt = 9, _Age = 45, GUIY = Guid.NewGuid(), Notes = "Enjoys treasure hunts", Access=Person.Permissions.CanRead},
                        new Person { Name = "Luna", TestInt = 9, _Age = 35, GUIY = Guid.NewGuid(), Notes = "Writes mystery novels", Access = Person.Permissions.CanRead|Person.Permissions.CanWrite},
                        new Person { Name = "Jerry", TestInt = 9, _Age = 35, GUIY = Guid.NewGuid(), Notes = "Collects vinyl records", Access = Person.Permissions.CanRead|Person.Permissions.CanWrite|Person.Permissions.CanCreate},
                        new Person { Name = "Jon", TestInt = 9, _Age = 37, GUIY = Guid.NewGuid(), Notes = "Runs a book club", Access = Person.Permissions.CanRead},
                        new Person { Name = "Jack", TestInt = 9, _Age = 37, GUIY = Guid.NewGuid(), Notes = "Builds model airplanes", Access = Person.Permissions.CanRead|Person.Permissions.CanWrite},
                        new Person { Name = "Cathy", TestInt = 9, _Age = 22, GUIY = Guid.NewGuid(), Notes = "Studies archaeology", Access = Person.Permissions.CanRead | Person.Permissions.CanWrite},
                        new Person { Name = "Bob", TestInt = 3 , _Age = 69, GUIY = Guid.NewGuid(), Notes = "Maintains a community garden", Access = Person.Permissions.CanRead },
                        new Person { Name = "Alex", TestInt = 3 , _Age = 80, GUIY = Guid.NewGuid(), Notes = "Paints landscapes" }
                    };
                    await personQuery.AddRangeAsync(persons);
                }

                var storageInfo = await _MagicDb.GetStorageEstimateAsync();
                storageQuota = storageInfo.QuotaInMegabytes;
                storageUsage = storageInfo.UsageInMegabytes;
                    
                allPeople = await personQuery.ToListAsync();

                WhereExample = (await personQuery.Where(x => x.Name.StartsWith("c", StringComparison.OrdinalIgnoreCase)
                || x.Name.StartsWith("l", StringComparison.OrdinalIgnoreCase)
                || x.Name.StartsWith("j", StringComparison.OrdinalIgnoreCase) && x._Age > 35
                ).Skip(1).ToListAsync()).OrderBy(x => x._Id);

                /*
                 * Still working on allowing nested
                 */
                //// Should return "Zack"
                //var NestedResult = await manager.Where<Person>(p => (p.Name == "Zack" || p.Name == "Luna") && (p._Age >= 35 && p._Age <= 45)).Execute();

                //// should return "Luna", "Jerry" and "Jon"
                //var NonNestedResult = await manager.Where<Person>(p => p.TestInt == 9 && p._Age >= 35 && p._Age <= 45).Execute();
                StateHasChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
