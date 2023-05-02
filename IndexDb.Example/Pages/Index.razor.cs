using Magic.IndexedDb.Models;
using System;

namespace IndexDb.Example.Pages
{
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
                    var manager = await _MagicDb.GetDbManager(DbNames.Client);

                    //await manager.ClearTable<Person>();

                    var AllThePeeps = await manager.GetAll<Person>();
                    if (AllThePeeps.Count() < 1)
                    {
                        Person[] persons = new Person[] {
                    new Person { Name = "Zack", TestInt = 9, _Age = 45, GUIY = Guid.NewGuid(), Secret = "I buried treasure behind my house"},
                    new Person { Name = "Luna", TestInt = 9, _Age = 35, GUIY = Guid.NewGuid(), Secret = "Jerry is my husband and I had an affair with Bob."},
                    new Person { Name = "Jerry", TestInt = 9, _Age = 35, GUIY = Guid.NewGuid(), Secret = "My wife is amazing"},
                    new Person { Name = "Jon", TestInt = 9, _Age = 37, GUIY = Guid.NewGuid(), Secret = "I black mail Luna for money because I know her secret"},
                    new Person { Name = "Jack", TestInt = 9, _Age = 37, GUIY = Guid.NewGuid(), Secret = "I have a drug problem"},
                    new Person { Name = "Cathy", TestInt = 9, _Age = 22, GUIY = Guid.NewGuid(), Secret = "I got away with reading Bobs diary."},
                    new Person { Name = "Bob", TestInt = 3 , _Age = 69, GUIY = Guid.NewGuid(), Secret = "I caught Cathy reading my diary, but I'm too shy to confront her." },
                    new Person { Name = "Alex", TestInt = 3 , _Age = 80, GUIY = Guid.NewGuid(), Secret = "I'm naked! But nobody can know!" }
                    };

                        await manager.AddRange(persons);
                    }


                    //var StorageLimit = await manager.GetStorageEstimateAsync();
                    var storageInfo = await manager.GetStorageEstimateAsync();
                    storageQuota = storageInfo.quota;
                    storageUsage = storageInfo.usage;

                    var allPeopleDecrypted = await manager.GetAll<Person>();

                    foreach (Person person in allPeopleDecrypted)
                    {
                        person.SecretDecrypted = await manager.Decrypt(person.Secret);
                        allPeople.Add(person);
                    }

                    WhereExample = await manager.Where<Person>(x => x.Name.StartsWith("c", StringComparison.OrdinalIgnoreCase)
                    || x.Name.StartsWith("l", StringComparison.OrdinalIgnoreCase)
                    || x.Name.StartsWith("j", StringComparison.OrdinalIgnoreCase) && x._Age > 35
                    ).OrderBy(x => x._Id).Skip(1).Execute();


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

                }

            }
        }
    }
}
