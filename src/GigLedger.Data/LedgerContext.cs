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
    public DbSet<ChargeSessionRow> ChargeSessions => Set<ChargeSessionRow>();
    public DbSet<TipRow> Tips => Set<TipRow>();
    public DbSet<HomeRateRow> HomeRates => Set<HomeRateRow>();
    public DbSet<DriveRow> Drives => Set<DriveRow>();
    public DbSet<ExpenseRow> Expenses => Set<ExpenseRow>();
    public DbSet<AttachmentRow> Attachments => Set<AttachmentRow>();
    public DbSet<MileageRateRow> MileageRates => Set<MileageRateRow>();
    public DbSet<PlatformFormRow> PlatformForms => Set<PlatformFormRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder conventions)
    {
        // Grades are stored by name, so the file reads plainly and an enum reorder cannot
        // silently regrade stored numbers.
        conventions.Properties<Core.Grade>().HaveConversion<string>();
        conventions.Properties<Core.ChargeType>().HaveConversion<string>();
        conventions.Properties<Core.Purpose>().HaveConversion<string>();
        conventions.Properties<Core.ExpenseCategory>().HaveConversion<string>();
        conventions.Properties<Core.AttachedTo>().HaveConversion<string>();
        conventions.Properties<Core.TaxForm>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ShiftCloseRow>().HasIndex(r => r.ShiftId);
        model.Entity<TripRow>().HasIndex(r => r.ShiftId);
        model.Entity<TripActualsRow>().HasIndex(r => r.TripId);
        model.Entity<TipRow>().HasIndex(r => r.TripId);
        model.Entity<DriveRow>().HasIndex(r => r.ShiftId);
        model.Entity<AttachmentRow>().HasIndex(r => new { r.Owner, r.OwnerId });

        // SRS 0.3: the placeholder home rate, until one is read from a bill.
        model.Entity<HomeRateRow>().HasData(new HomeRateRow
        {
            Id = new Guid("5e771495-0000-4000-8000-000000000002"),
            RecordedAt = new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.FromHours(-5)),
            PerKwh = Core.HomeRate.Placeholder.PerKwh,
            EffectiveFrom = Core.HomeRate.Placeholder.EffectiveFrom,
            IsPlaceholder = true,
        });

        // The confirmed starting settings (SDD section 11), stored as the first version.
        model.Entity<SettingsRow>().HasData(new SettingsRow
        {
            Id = new Guid("5e771495-0000-4000-8000-000000000001"),
            RecordedAt = new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.FromHours(-5)),
            AcceptThreshold = Core.Settings.Initial.AcceptThreshold,
            DefaultMilesPerKwh = Core.Settings.Initial.DefaultMilesPerKwh,
            DefaultPricePerKwh = Core.Settings.Initial.DefaultPricePerKwh,
        });
    }

    /// <summary>
    /// SDD 5.3: a ledger row is inserted once and never changed. This stops updates and
    /// deletes made through EF; the database's own triggers stop the ones that are not.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RefuseChangesToStoredRows();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RefuseChangesToStoredRows();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void RefuseChangesToStoredRows()
    {
        var changed = ChangeTracker.Entries<LedgerRecord>()
            .Where(e => e.State is EntityState.Modified or EntityState.Deleted)
            .Select(e => $"{e.Entity.GetType().Name} {e.Entity.Id} ({e.State})")
            .ToList();
        if (changed.Count > 0)
            throw new InvalidOperationException(
                "Ledger rows are never updated or deleted; store a correction instead: " + string.Join(", ", changed));
    }
}

/// <summary>Lets `dotnet ef` build the context at design time, for migrations.</summary>
public sealed class LedgerContextFactory : IDesignTimeDbContextFactory<LedgerContext>
{
    public LedgerContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<LedgerContext>().UseSqlite("Data Source=design.db").Options);
}
