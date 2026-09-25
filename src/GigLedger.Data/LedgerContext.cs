using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GigLedger.Data;

public sealed class LedgerContext(DbContextOptions<LedgerContext> options) : DbContext(options)
{
    public DbSet<ShiftRow> Shifts => Set<ShiftRow>();
    public DbSet<ShiftCloseRow> ShiftCloses => Set<ShiftCloseRow>();
    public DbSet<TripRow> Trips => Set<TripRow>();
    public DbSet<TripActualsRow> TripActuals => Set<TripActualsRow>();
    public DbSet<SettingsRow> Settings => Set<SettingsRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder conventions)
    {
        // Grades are stored by name, so the file reads plainly and an enum reorder cannot
        // silently regrade stored numbers.
        conventions.Properties<Core.Grade>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ShiftCloseRow>().HasIndex(r => r.ShiftId);
        model.Entity<TripRow>().HasIndex(r => r.ShiftId);
        model.Entity<TripActualsRow>().HasIndex(r => r.TripId);
    }
}

/// <summary>Lets `dotnet ef` build the context at design time, for migrations.</summary>
public sealed class LedgerContextFactory : IDesignTimeDbContextFactory<LedgerContext>
{
    public LedgerContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<LedgerContext>().UseSqlite("Data Source=design.db").Options);
}
