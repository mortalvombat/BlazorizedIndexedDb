using Blazorized.IndexedDb.Models;
using IndexDb.Example.Models;
using System;
using System.Linq.Expressions;
using System.Runtime.InteropServices;

namespace IndexDb.Example.Pages
{
    public partial class Index
    {
        private List<Person> AllPeople { get; set; } = new();
        private List<ManualDto> ManualDtos { get; set; } = new();

        private IEnumerable<Person> WhereExample { get; set; } = Enumerable.Empty<Person>();

        private double StorageQuota { get; set; }
        private double StorageUsage { get; set; }

        private const string PersonName = "Bob";
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {

                try
                {
                    var manager = await _BlazorizedDb.GetDbManager(DbNames.Client);

                    //await manager.ClearTable<Person>();
                    var personName = "Bob";
                    Expression<Func<Person, bool>> predicate = p => p.Name == PersonName;
                    var AllThePeeps = (await manager.Where<Person>(predicate).Execute()).ToList();
                    //r AllThePeeps = await manager.GetAll<Person>();
                    if (AllThePeeps.Count < 1)
                    {
                        Person[] persons = [
                    new() { Name = "Zack", TestInt = 9, _Age = 45, GUIY = Guid.NewGuid(), Secret = "I buried treasure behind my house"},
                    new() { Name = "Luna", TestInt = 9, _Age = 35, GUIY = Guid.NewGuid(), Secret = "Jerry is my husband and I had an affair with Bob."},
                    new() { Name = "Jerry", TestInt = 9, _Age = 35, GUIY = Guid.NewGuid(), Secret = "My wife is amazing"},
                    new() { Name = "Jon", TestInt = 9, _Age = 37, GUIY = Guid.NewGuid(), Secret = "I black mail Luna for money because I know her secret"},
                    new() { Name = "Jack", TestInt = 9, _Age = 37, GUIY = Guid.NewGuid(), Secret = "I have a drug problem"},
                    new() { Name = "Cathy", TestInt = 9, _Age = 22, GUIY = Guid.NewGuid(), Secret = "I got away with reading Bobs diary."},
                    new() { Name = "Bob", TestInt = 3 , _Age = 69, GUIY = Guid.NewGuid(), Secret = "I caught Cathy reading my diary, but I'm too shy to confront her." },
                    new() { Name = "Alex", TestInt = 3 , _Age = 80, GUIY = Guid.NewGuid(), Secret = "I'm naked! But nobody can know!" }
                    ];

                        //await manager.AddRange(persons);
                        foreach (var p in persons)
                        {
                            await manager.Add(p);
                        }
                    }

                    var manualDtos = await manager.GetAll<ManualDto>();

                    int manualDtoId = 5;
                    var manualDto = await manager.GetById<ManualDto>(manualDtoId);
                    if (manualDto == null)
                    {
                        var newDto = new ManualDto
                        {
                            Id = manualDtoId,
                            Name = "Foobar"
                        };
                        await manager.Add(newDto);
                        manualDto = await manager.GetById<ManualDto>(manualDtoId);
                    }

                    //int manualDtoId = 5999767;
                    //var manualDto = await manager.GetById<Person>(manualDtoId);
                    //if (manualDto == null)
                    //{
                    //    manualDto = new() { Name = "Foo", TestInt = 8, _Age = 5, GUIY = Guid.NewGuid(), Secret = "I like foobar!" };
                    //    await manager.Add(manualDto);
                    //}

                    //var StorageLimit = await manager.GetStorageEstimateAsync();
                    var (quota, usage) = await manager.GetStorageEstimateAsync();
                    StorageQuota = quota;
                    StorageUsage = usage;

                    var allPeopleDecrypted = await manager.GetAll<Person>();

                    foreach (Person person in allPeopleDecrypted)
                    {
                        person.SecretDecrypted = await manager.Decrypt(person.Secret);
                        AllPeople.Add(person);
                    }

                    AllPeople.Add(new Person
                    {
                        id = manualDto.Id,
                        Name = manualDto.Name
                    });

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
                    Console.WriteLine(ex.Message);
                }

            }
        }
    }
}
