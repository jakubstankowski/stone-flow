using Microsoft.EntityFrameworkCore;
using StoneFlow.Shared.Application.Scheduling;

namespace StoneFlow.Shared.Infrastructure.Scheduling;

/// <summary>
/// Stan harmonogramu zadań (SF-11), schemat <c>scheduling</c>. Kontekst ma własne połączenie (nie
/// <c>ModuleConnection</c>), więc zapisy harmonogramu nie zależą od jednostek pracy zadań.
/// </summary>
public sealed class SchedulingDbContext(DbContextOptions<SchedulingDbContext> options) : DbContext(options)
{
    public const string Schema = "scheduling";

    public DbSet<JobState> Jobs => Set<JobState>();

    public DbSet<JobRun> JobRuns => Set<JobRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<JobState>(job =>
        {
            job.ToTable("Jobs");
            job.HasKey(j => j.JobKey);
            job.Property(j => j.JobKey).HasMaxLength(JobDefinition.MaxKeyLength);
            job.Property(j => j.ConsecutiveFailures).HasDefaultValue(0);
        });

        modelBuilder.Entity<JobRun>(run =>
        {
            run.ToTable("JobRuns");
            run.HasKey(r => r.Id);
            run.Property(r => r.Id).ValueGeneratedNever();
            run.Property(r => r.JobKey).HasMaxLength(JobDefinition.MaxKeyLength);
            run.Property(r => r.Trigger).HasMaxLength(20);
            run.Property(r => r.Status).HasMaxLength(20);
            run.Property(r => r.Error).HasMaxLength(JobDue.MaxErrorLength);
            run.HasOne<JobState>().WithMany().HasForeignKey(r => r.JobKey).OnDelete(DeleteBehavior.Restrict);
            run.HasIndex(r => new { r.JobKey, r.StartedAtUtc }).IsDescending(false, true);
        });
    }
}

/// <summary>Wiersz stanu zadania; czasy w UTC (<c>datetime2</c>).</summary>
public sealed class JobState
{
    public required string JobKey { get; init; }

    public DateTime RegisteredAtUtc { get; init; }

    public DateTime? LastSuccessStartedAtUtc { get; set; }

    public DateTime? LastAttemptStartedAtUtc { get; set; }

    public int ConsecutiveFailures { get; set; }

    public DateTime? NextAttemptAtUtc { get; set; }

    public Guid? LeaseRunId { get; set; }

    public DateTime? LeaseExpiresAtUtc { get; set; }
}

/// <summary>Przebieg zadania w historii; kody źródła i statusu z <see cref="JobCodes"/>.</summary>
public sealed class JobRun
{
    public Guid Id { get; init; }

    public required string JobKey { get; init; }

    public required string Trigger { get; init; }

    public DateTime DueAtUtc { get; init; }

    public DateTime StartedAtUtc { get; init; }

    public DateTime? FinishedAtUtc { get; set; }

    public required string Status { get; set; }

    public string? Error { get; set; }
}
