using MatDo.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MatDo.Data;

/// <summary>
/// The database (PostgreSQL). Tables are PascalCase and singular, the key is always <c>Id</c> (BIGINT), every record has CreateDate, CreateUserId, UpdateDate and UpdateUserId
/// (filled in <see cref="SaveChangesAsync(bool, CancellationToken)"/>), and a record with an <c>UpdateState</c> is deleted softly (0 = deleted, 1 = created, 2 = updated).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, CurrentUser? current = null) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<UserSession> Sessions => Set<UserSession>();

    public DbSet<UserKeyword> Keywords => Set<UserKeyword>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Section> Sections => Set<Section>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<Label> Labels => Set<Label>();

    public DbSet<TaskLabel> TaskLabels => Set<TaskLabel>();

    public DbSet<TaskComment> Comments => Set<TaskComment>();

    public DbSet<WebhookToken> Webhooks => Set<WebhookToken>();

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<PushDevice> PushDevices => Set<PushDevice>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("User");
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.DisplayName).HasMaxLength(120);
            e.Property(x => x.TimeZone).HasMaxLength(64);
            e.Property(x => x.Language).HasMaxLength(5);
            e.Property(x => x.Theme).HasMaxLength(10);
            e.Property(x => x.Scheme).HasMaxLength(10);
            e.Property(x => x.ExternalSubject).HasMaxLength(200);
            e.HasIndex(x => x.ExternalSubject).IsUnique().HasFilter("\"ExternalSubject\" IS NOT NULL");
            e.HasQueryFilter(x => x.UpdateState != SoftEntity.Deleted);
        });

        b.Entity<UserSession>(e =>
        {
            e.ToTable("UserSession");
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.UserAgent).HasMaxLength(300);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.IdentitySessionId).HasMaxLength(200);
            e.HasIndex(x => x.IdentitySessionId);
        });

        b.Entity<UserKeyword>(e =>
        {
            e.ToTable("UserKeyword");
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Word).HasMaxLength(40);
            e.HasQueryFilter(x => x.UpdateState != SoftEntity.Deleted);
        });

        b.Entity<Team>(e =>
        {
            e.ToTable("Team");
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Color).HasMaxLength(7);
            e.HasQueryFilter(x => x.UpdateState != SoftEntity.Deleted);
        });

        b.Entity<TeamMember>(e =>
        {
            e.ToTable("TeamMember");
            e.HasIndex(x => new { x.TeamId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.Team).WithMany(t => t.Members).HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Project>(e =>
        {
            e.ToTable("Project");
            e.HasIndex(x => new { x.OwnerId, x.TeamId });
            e.HasIndex(x => x.TeamId);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Color).HasMaxLength(7);
            e.HasQueryFilter(x => x.UpdateState != SoftEntity.Deleted);
        });

        b.Entity<Section>(e =>
        {
            e.ToTable("Section");
            e.HasIndex(x => x.ProjectId);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasQueryFilter(x => x.UpdateState != SoftEntity.Deleted);
        });

        b.Entity<TaskItem>(e =>
        {
            e.ToTable("Task");
            e.HasIndex(x => new { x.ProjectId, x.IsCompleted });
            e.HasIndex(x => x.DueDate);
            e.HasIndex(x => x.ParentId);
            e.HasIndex(x => x.AssigneeId);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Section).WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Content).HasMaxLength(500);
            e.Property(x => x.Description).HasMaxLength(10000);
            e.Property(x => x.Recurrence).HasMaxLength(60);
            e.Property(x => x.RecurrenceText).HasMaxLength(120);
            e.HasQueryFilter(x => x.UpdateState != SoftEntity.Deleted);
        });

        b.Entity<Label>(e =>
        {
            e.ToTable("Label");
            e.HasIndex(x => new { x.OwnerId, x.Name }).IsUnique().HasFilter("\"UpdateState\" <> 0");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Color).HasMaxLength(7);
            e.HasQueryFilter(x => x.UpdateState != SoftEntity.Deleted);
        });

        b.Entity<TaskLabel>(e =>
        {
            e.ToTable("TaskLabel");
            e.HasIndex(x => new { x.TaskId, x.LabelId }).IsUnique();
            e.HasIndex(x => x.LabelId);
            e.HasOne(x => x.Task).WithMany(t => t.Labels).HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Label).WithMany().HasForeignKey(x => x.LabelId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TaskComment>(e =>
        {
            e.ToTable("TaskComment");
            e.HasIndex(x => x.TaskId);
            e.HasOne<TaskItem>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Text).HasMaxLength(5000);
            e.HasQueryFilter(x => x.UpdateState != SoftEntity.Deleted);
        });

        b.Entity<WebhookToken>(e =>
        {
            e.ToTable("WebhookToken");
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasQueryFilter(x => x.UpdateState != SoftEntity.Deleted);
        });

        b.Entity<SystemSetting>(e =>
        {
            e.ToTable("SystemSetting");
            e.Property(x => x.PublicUrl).HasMaxLength(300);
            e.Property(x => x.VapidPublicKey).HasMaxLength(100);
            e.Property(x => x.VapidPrivateKey).HasMaxLength(100);
        });

        b.Entity<PushDevice>(e =>
        {
            e.ToTable("PushDevice");
            e.HasIndex(x => x.Endpoint).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Endpoint).HasMaxLength(1000);
            e.Property(x => x.P256dh).HasMaxLength(200);
            e.Property(x => x.Auth).HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Language).HasMaxLength(10);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Stamp();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Fills the audit columns, and turns the delete of a soft record into UpdateState 0.</summary>
    private void Stamp()
    {
        var now = DateTime.UtcNow;
        var user = current?.Id;
        foreach (var entry in ChangeTracker.Entries<Entity>().ToList())
        {
            var entity = entry.Entity;
            if (entry.State == EntityState.Deleted && entity is SoftEntity soft)
            {
                entry.State = EntityState.Modified;
                soft.UpdateState = SoftEntity.Deleted;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    if (entity.CreateDate == default)
                    {
                        entity.CreateDate = now;
                    }

                    entity.CreateUserId ??= user;
                    entity.UpdateDate = now;
                    entity.UpdateUserId = user ?? entity.CreateUserId;
                    break;
                case EntityState.Modified:
                    entity.UpdateDate = now;
                    entity.UpdateUserId = user ?? entity.UpdateUserId;
                    if (entity is SoftEntity { UpdateState: not SoftEntity.Deleted } updated)
                    {
                        updated.UpdateState = SoftEntity.Updated;
                    }

                    break;
            }
        }
    }
}

/// <summary>For <c>dotnet ef migrations add</c>: no database is needed to create a migration.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=matdo;Username=matdo;Password=matdo").Options);
}
