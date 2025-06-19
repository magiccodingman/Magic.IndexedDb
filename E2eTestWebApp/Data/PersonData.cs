using E2eTestWebApp.Models;

namespace E2eTestWebApp.Data;

public static class PersonData
{
    public static Person[] persons =
    [
        new Person { Name = "Zack", DateOfBirth = null, TestInt = 9, _Age = 45, GUIY = Guid.NewGuid(), DoNotMapTest = "I buried treasure behind my house", Access=Person.Permissions.CanRead},
        new Person { Name = "Luna", TestInt = 9, DateOfBirth = new DateTime(1980, 1, 1), _Age = 45, GUIY = Guid.NewGuid(), DoNotMapTest = "Jerry is my husband and I had an affair with Bob.", Access = Person.Permissions.CanRead|Person.Permissions.CanWrite},
        new Person { Name = "Jerry", TestInt = 9, DateOfBirth = new DateTime(1981, 1, 1), _Age = 44, GUIY = Guid.NewGuid(), DoNotMapTest = "My wife is amazing", Access = Person.Permissions.CanRead|Person.Permissions.CanWrite|Person.Permissions.CanCreate},
        new Person { Name = "Jamie", TestInt = 9, DateOfBirth = new DateTime(1982, 1, 1), _Age = 43, GUIY = Guid.NewGuid(), DoNotMapTest = "My wife is amazing", Access = Person.Permissions.CanRead|Person.Permissions.CanWrite|Person.Permissions.CanCreate},
        new Person { Name = "James", TestInt = 9, DateOfBirth = new DateTime(1983, 1, 1), _Age = 42, GUIY = Guid.NewGuid(), DoNotMapTest = "My wife is amazing", Access = Person.Permissions.CanRead|Person.Permissions.CanWrite|Person.Permissions.CanCreate},
        new Person { Name = "Jack", TestInt = 9, DateOfBirth = new DateTime(1984, 1, 1), _Age = 41, GUIY = Guid.NewGuid(), DoNotMapTest = "My wife is amazing", Access = Person.Permissions.CanRead|Person.Permissions.CanWrite|Person.Permissions.CanCreate},
        new Person { Name = "Jon", TestInt = 9, DateOfBirth = new DateTime(1985, 1, 1), _Age = 40, GUIY = Guid.NewGuid(), DoNotMapTest = "I black mail Luna for money because I know her secret", Access = Person.Permissions.CanRead},
        new Person { Name = "Jack", TestInt = 9, DateOfBirth = new DateTime(1986, 1, 1), _Age = 39, GUIY = Guid.NewGuid(), DoNotMapTest = "I have a drug problem", Access = Person.Permissions.CanRead|Person.Permissions.CanWrite},
        new Person { Name = "Cathy", TestInt = 9, DateOfBirth = new DateTime(1987, 1, 1), _Age = 38, GUIY = Guid.NewGuid(), DoNotMapTest = "I got away with reading Bobs diary.", Access = Person.Permissions.CanRead | Person.Permissions.CanWrite},
        new Person { Name = "Bob", TestInt = 3 , DateOfBirth = new DateTime(1988, 1, 1), _Age = 37, GUIY = Guid.NewGuid(), DoNotMapTest = "I caught Cathy reading my diary, but I'm too shy to confront her.", Access = Person.Permissions.CanRead },
        new Person { Name = "Alex", TestInt = 3 , DateOfBirth = null, _Age = 80, GUIY = Guid.NewGuid(), DoNotMapTest = "I'm naked! But nobody can know!" },
        new Person { Name = "Zapoo", DateOfBirth = null, TestInt = 9, _Age = 45, GUIY = Guid.NewGuid(), DoNotMapTest = "I buried treasure behind my house", Access=Person.Permissions.CanRead},

        new Person { Name = "Sarah", TestInt = -1, _Age = 30, GUIY = Guid.NewGuid(), DoNotMapTest = "I hate my job", Access=Person.Permissions.CanRead},
        new Person { Name = "Michael", TestInt = 15, _Age = 50, GUIY = Guid.NewGuid(), DoNotMapTest = "I'm hiding a big secret", Access=Person.Permissions.CanRead | Person.Permissions.CanWrite},
        new Person { Name = "Tommy", TestInt = 7, _Age = 12, GUIY = Guid.NewGuid(), DoNotMapTest = "I am just a kid" },
        new Person { Name = "Grace", TestInt = 3, _Age = 90, GUIY = Guid.NewGuid(), DoNotMapTest = "I have seen the world" },
        new Person { Name = "Xylophone", TestInt = 9, _Age = 27, GUIY = Guid.NewGuid(), DoNotMapTest = "I have the weirdest name" },
        new Person { Name = "Yasmine", TestInt = 9, _Age = 40, GUIY = Guid.NewGuid(), DoNotMapTest = null },
        
        // Additional test case persons to stress-test LINQ validation
        new Person { Name = "Alicia", TestInt = 42, _Age = 16, GUIY = Guid.NewGuid(), DoNotMapTest = "I just got my driver's license" },
        new Person { Name = "Ben", TestInt = 0, _Age = 25, GUIY = Guid.NewGuid(), DoNotMapTest = "I have no TestInt value" },
        new Person { Name = "Clara", TestInt = 100, _Age = 65, GUIY = Guid.NewGuid(), DoNotMapTest = "I retired last week", Access = Person.Permissions.CanRead | Person.Permissions.CanWrite },
        new Person { Name = "Danny", TestInt = 9, _Age = 40, GUIY = Guid.NewGuid(), DoNotMapTest = null }, // Null handling
        new Person { Name = "Elliot", TestInt = -20, _Age = 55, GUIY = Guid.NewGuid(), DoNotMapTest = "My test int is negative" },
        new Person { Name = "Fiona", TestInt = 11, _Age = 33, GUIY = Guid.NewGuid(), DoNotMapTest = "I like puzzles" },
        new Person { Name = "George", TestInt = 8, _Age = 72, GUIY = Guid.NewGuid(), DoNotMapTest = "I fought in a war", Access = Person.Permissions.CanRead | Person.Permissions.CanWrite | Person.Permissions.CanCreate },
        new Person { Name = "Henry", TestInt = 99, _Age = 29, GUIY = Guid.NewGuid(), DoNotMapTest = "I almost made it to 100 TestInt" },
        new Person { Name = "Isla", TestInt = 2, _Age = 18, GUIY = Guid.NewGuid(), DoNotMapTest = "I just turned into an adult" },
        new Person { Name = "Jackie", TestInt = 75, _Age = 60, GUIY = Guid.NewGuid(), DoNotMapTest = "I love cooking" },
        new Person { Name = "Kevin", TestInt = 5, _Age = 48, GUIY = Guid.NewGuid(), DoNotMapTest = "I own a small business" },
        new Person { Name = "Liam", TestInt = 9, _Age = 55, GUIY = Guid.NewGuid(), DoNotMapTest = "I just became a grandfather" },
        new Person { Name = "Mona", TestInt = 88, _Age = 35, GUIY = Guid.NewGuid(), DoNotMapTest = "I am a detective", Access = Person.Permissions.CanRead | Person.Permissions.CanWrite },
        new Person { Name = "Nathan", TestInt = 7, _Age = 27, GUIY = Guid.NewGuid(), DoNotMapTest = "I play guitar" },
        new Person { Name = "Olivia", TestInt = 13, _Age = 45, GUIY = Guid.NewGuid(), DoNotMapTest = "I run marathons" },
        new Person { Name = "Patrick", TestInt = 3, _Age = 52, GUIY = Guid.NewGuid(), DoNotMapTest = "I work in IT" },
        new Person { Name = "Quinn", TestInt = 22, _Age = 42, GUIY = Guid.NewGuid(), DoNotMapTest = "I design board games" },
        new Person { Name = "Rachel", TestInt = 77, _Age = 36, GUIY = Guid.NewGuid(), DoNotMapTest = "I am a pilot" },
        new Person { Name = "Steve", TestInt = 9, _Age = 38, GUIY = Guid.NewGuid(), DoNotMapTest = "I am an engineer" },
        new Person { Name = "Tina", TestInt = 3, _Age = 68, GUIY = Guid.NewGuid(), DoNotMapTest = "I just got my pension" },
        new Person { Name = "Uma", TestInt = 14, _Age = 39, GUIY = Guid.NewGuid(), DoNotMapTest = "I teach yoga" },
        new Person { Name = "Victor", TestInt = 6, _Age = 31, GUIY = Guid.NewGuid(), DoNotMapTest = "I am an artist" },
        new Person { Name = "Wendy", TestInt = 50, _Age = 50, GUIY = Guid.NewGuid(), DoNotMapTest = "My age matches my test int" },
        new Person { Name = "Xander", TestInt = 19, _Age = 21, GUIY = Guid.NewGuid(), DoNotMapTest = "I am a college student" },
        new Person { Name = "Yara", TestInt = 90, _Age = 32, GUIY = Guid.NewGuid(), DoNotMapTest = "I work in finance" },
        new Person { Name = "Zane", TestInt = 101, _Age = 47, DateOfBirth = new DateTime(2020, 2, 10), GUIY = Guid.NewGuid(), DoNotMapTest = "I love motorcycles" }
    ];
}