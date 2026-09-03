using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace PostgNet.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL";
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("postgnet_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        await SeedAsync(context);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    internal TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new TestDbContext(options);
    }

    private static async Task SeedAsync(TestDbContext context)
    {
        var ada = new User
        {
            Id = 1,
            Name = "Ada",
            IsActive = true,
        };
        var grace = new User
        {
            Id = 2,
            Name = "Grace",
            Nickname = "Amazing Grace",
            IsActive = false,
        };
        var linus = new User
        {
            Id = 3,
            Name = "Linus",
            Nickname = "Lin",
            IsActive = true,
        };
        var lathe = new Machine { Id = 10, Name = "Lathe" };
        var press = new Machine { Id = 20, Name = "Press" };

        context.AddRange(ada, grace, linus, lathe, press);
        context.AddRange(
            new Intervention
            {
                Id = 100,
                Title = "Inspect",
                User = ada,
                Machine = lathe,
            },
            new Intervention
            {
                Id = 101,
                Title = "Repair",
                User = ada,
                Machine = press,
            },
            new Intervention
            {
                Id = 102,
                Title = "Calibrate",
                User = grace,
                Machine = lathe,
            }
        );
        await context.SaveChangesAsync();
    }
}
