using Microsoft.EntityFrameworkCore;

namespace PostgNet.IntegrationTests;

internal sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Intervention> Interventions => Set<Intervention>();

    public DbSet<Machine> Machines => Set<Machine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(user => user.Name).HasColumnName("name");
            entity.Property(user => user.Nickname).HasColumnName("nickname");
            entity.Property(user => user.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<Machine>(entity =>
        {
            entity.ToTable("machines");
            entity.HasKey(machine => machine.Id);
            entity.Property(machine => machine.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(machine => machine.Name).HasColumnName("name");
        });

        modelBuilder.Entity<Intervention>(entity =>
        {
            entity.ToTable("interventions");
            entity.HasKey(intervention => intervention.Id);
            entity
                .Property(intervention => intervention.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();
            entity.Property(intervention => intervention.Title).HasColumnName("title");
            entity.Property(intervention => intervention.UserId).HasColumnName("user_id");
            entity.Property(intervention => intervention.MachineId).HasColumnName("machine_id");

            entity
                .HasOne(intervention => intervention.User)
                .WithMany(user => user.Interventions)
                .HasForeignKey(intervention => intervention.UserId)
                .IsRequired();
            entity
                .HasOne(intervention => intervention.Machine)
                .WithMany(machine => machine.Interventions)
                .HasForeignKey(intervention => intervention.MachineId)
                .IsRequired();
        });
    }
}

internal sealed class User
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Nickname { get; set; }

    public bool IsActive { get; set; }

    public List<Intervention> Interventions { get; set; } = [];
}

internal sealed class Machine
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<Intervention> Interventions { get; set; } = [];
}

internal sealed class Intervention
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public int MachineId { get; set; }

    public Machine Machine { get; set; } = null!;
}
