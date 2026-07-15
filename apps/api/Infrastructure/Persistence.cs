using GoldenHour.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GoldenHour.Api.Infrastructure;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string PreferredLanguage { get; set; } = "en";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class GoldenHourDbContext(
    DbContextOptions<GoldenHourDbContext> options,
    TimeProvider timeProvider) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<EmergencyProfile> EmergencyProfiles => Set<EmergencyProfile>();
    public DbSet<EmergencyContact> EmergencyContacts => Set<EmergencyContact>();
    public DbSet<Allergy> Allergies => Set<Allergy>();
    public DbSet<MedicalCondition> MedicalConditions => Set<MedicalCondition>();
    public DbSet<Medication> Medications => Set<Medication>();
    public DbSet<MedicalProcedure> MedicalProcedures => Set<MedicalProcedure>();
    public DbSet<PreferredHospital> PreferredHospitals => Set<PreferredHospital>();
    public DbSet<SharingPreference> SharingPreferences => Set<SharingPreference>();
    public DbSet<EmergencySession> EmergencySessions => Set<EmergencySession>();
    public DbSet<EmergencyParticipant> EmergencyParticipants => Set<EmergencyParticipant>();
    public DbSet<EmergencyObservation> EmergencyObservations => Set<EmergencyObservation>();
    public DbSet<EmergencyTask> EmergencyTasks => Set<EmergencyTask>();
    public DbSet<EmergencyTimelineEvent> EmergencyTimelineEvents => Set<EmergencyTimelineEvent>();
    public DbSet<EmergencyLocation> EmergencyLocations => Set<EmergencyLocation>();
    public DbSet<EmergencySummary> EmergencySummaries => Set<EmergencySummary>();
    public DbSet<EmergencyShareToken> EmergencyShareTokens => Set<EmergencyShareToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<AiOperation> AiOperations => Set<AiOperation>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<WebhookReceipt> WebhookReceipts => Set<WebhookReceipt>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("golden_hour");

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.PreferredLanguage).HasMaxLength(12);
            entity.HasIndex(x => x.NormalizedEmail);
        });

        ConfigureEntity<EmergencyProfile>(builder);
        ConfigureEntity<EmergencyContact>(builder);
        ConfigureEntity<Allergy>(builder);
        ConfigureEntity<MedicalCondition>(builder);
        ConfigureEntity<Medication>(builder);
        ConfigureEntity<MedicalProcedure>(builder);
        ConfigureEntity<PreferredHospital>(builder);
        ConfigureEntity<SharingPreference>(builder);
        ConfigureEntity<EmergencySession>(builder);
        ConfigureEntity<EmergencyParticipant>(builder);
        ConfigureEntity<EmergencyObservation>(builder);
        ConfigureEntity<EmergencyTask>(builder);
        ConfigureEntity<EmergencyTimelineEvent>(builder);
        ConfigureEntity<EmergencyLocation>(builder);
        ConfigureEntity<EmergencySummary>(builder);
        ConfigureEntity<EmergencyShareToken>(builder);
        ConfigureEntity<RefreshToken>(builder);
        ConfigureEntity<NotificationDelivery>(builder);
        ConfigureEntity<AiOperation>(builder);
        ConfigureEntity<AuditEvent>(builder);
        ConfigureEntity<OutboxMessage>(builder);
        ConfigureEntity<WebhookReceipt>(builder);

        builder.Entity<EmergencyProfile>(entity =>
        {
            entity.HasIndex(x => x.OwnerId).IsUnique();
            entity.Property(x => x.FullName).HasMaxLength(160);
            entity.Property(x => x.PreferredLanguage).HasMaxLength(12);
            entity.HasMany(x => x.Contacts).WithOne().HasForeignKey(x => x.EmergencyProfileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Allergies).WithOne().HasForeignKey(x => x.EmergencyProfileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Conditions).WithOne().HasForeignKey(x => x.EmergencyProfileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Medications).WithOne().HasForeignKey(x => x.EmergencyProfileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Procedures).WithOne().HasForeignKey(x => x.EmergencyProfileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.PreferredHospital).WithOne().HasForeignKey<PreferredHospital>(x => x.EmergencyProfileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.SharingPreference).WithOne().HasForeignKey<SharingPreference>(x => x.EmergencyProfileId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<EmergencyContact>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.PhoneNumber).HasMaxLength(40);
            entity.HasIndex(x => new { x.EmergencyProfileId, x.PhoneNumber }).IsUnique();
        });

        builder.Entity<EmergencySession>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.PatientRelationship).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.SelectedCategory).HasConversion<string>().HasMaxLength(48);
            entity.HasIndex(x => new { x.OwnerId, x.Status, x.UpdatedAtUtc });
            entity.HasMany(x => x.Participants).WithOne().HasForeignKey(x => x.EmergencySessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Observations).WithOne().HasForeignKey(x => x.EmergencySessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Tasks).WithOne().HasForeignKey(x => x.EmergencySessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Timeline).WithOne().HasForeignKey(x => x.EmergencySessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Locations).WithOne().HasForeignKey(x => x.EmergencySessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Summaries).WithOne().HasForeignKey(x => x.EmergencySessionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<EmergencyParticipant>(entity =>
        {
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => new { x.EmergencySessionId, x.UserId }).IsUnique();
        });

        builder.Entity<EmergencyTask>(entity =>
        {
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.TaskCode).HasMaxLength(80);
            entity.HasIndex(x => new { x.EmergencySessionId, x.Status });
            entity.HasIndex(x => new { x.EmergencySessionId, x.TaskCode }).IsUnique();
        });

        builder.Entity<EmergencyTimelineEvent>(entity =>
        {
            entity.Property(x => x.Type).HasMaxLength(80);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(128);
            entity.HasIndex(x => new { x.EmergencySessionId, x.Sequence }).IsUnique();
            entity.HasIndex(x => new { x.EmergencySessionId, x.IdempotencyKey }).IsUnique();
        });

        builder.Entity<EmergencySummary>(entity =>
        {
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => new { x.EmergencySessionId, x.Kind, x.CreatedAtUtc });
        });

        builder.Entity<EmergencyShareToken>(entity =>
        {
            entity.Property(x => x.TokenHash).HasMaxLength(32);
            entity.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.EmergencySessionId, x.ExpiresAtUtc });
            entity.HasOne<EmergencyParticipant>().WithMany().HasForeignKey(x => x.EmergencyParticipantId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RefreshToken>(entity =>
        {
            entity.Property(x => x.TokenHash).HasMaxLength(32);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.FamilyId, x.ExpiresAtUtc });
        });

        builder.Entity<NotificationDelivery>().HasIndex(x => new { x.Provider, x.ProviderMessageId }).IsUnique();
        builder.Entity<AiOperation>().HasIndex(x => new { x.EmergencySessionId, x.CreatedAtUtc });
        builder.Entity<AuditEvent>().HasIndex(x => new { x.ResourceType, x.ResourceId, x.CreatedAtUtc });
        builder.Entity<OutboxMessage>().HasIndex(x => new { x.ProcessedAtUtc, x.NextAttemptAtUtc });
        builder.Entity<WebhookReceipt>().HasIndex(x => new { x.Provider, x.DeliveryId }).IsUnique();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyUtcMetadata();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyUtcMetadata();
        return base.SaveChanges();
    }

    private void ApplyUtcMetadata()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.ConcurrencyToken = Guid.NewGuid();
            }
        }

        foreach (var entry in ChangeTracker.Entries<ApplicationUser>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
            }
        }
    }

    private static void ConfigureEntity<TEntity>(ModelBuilder builder) where TEntity : Entity
    {
        builder.Entity<TEntity>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
    }
}

public sealed class GoldenHourDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GoldenHourDbContext>
{
    public GoldenHourDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__GoldenHour")
            ?? "Host=localhost;Port=5432;Database=goldenhour;Username=goldenhour;Password=development-only";
        var options = new DbContextOptionsBuilder<GoldenHourDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(GoldenHourDbContext).Assembly.FullName))
            .Options;
        return new GoldenHourDbContext(options, TimeProvider.System);
    }
}
