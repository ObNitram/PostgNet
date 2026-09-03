using Microsoft.EntityFrameworkCore;

namespace PostgNet.Tests;

internal sealed class TestDbContext : DbContext
{
    public TestDbContext()
        : base(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
                .Options
        ) { }

    public DbSet<TestUser> Users => Set<TestUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestUser>(entity =>
        {
            entity.ToTable("app_users", "test");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).HasColumnName("user_id");
            entity.Property(user => user.Name).HasColumnName("display_name");
        });

        modelBuilder.Entity<TestIntervention>(entity =>
        {
            entity.ToTable("interventions", "test");
            entity.HasKey(intervention => intervention.Id);
            entity
                .HasOne(intervention => intervention.User)
                .WithMany(user => user.Interventions)
                .HasForeignKey(intervention => intervention.UserId)
                .IsRequired();
        });
    }
}

internal sealed class TestUser
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Nickname { get; set; }

    public bool IsActive { get; set; }

    public List<TestIntervention> Interventions { get; set; } = [];
}

internal sealed class TestIntervention
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public TestUser User { get; set; } = null!;
}

internal sealed class UserProjection
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

internal sealed class FilteredTestDbContext : DbContext
{
    public FilteredTestDbContext()
        : base(
            new DbContextOptionsBuilder<FilteredTestDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
                .Options
        ) { }

    public DbSet<FilteredUser> Users => Set<FilteredUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FilteredUser>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.HasQueryFilter(user => user.IsActive);
        });
    }
}

internal sealed class FilteredUser
{
    public int Id { get; set; }

    public bool IsActive { get; set; }
}
