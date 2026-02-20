using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Request> Requests => Set<Request>();
    public DbSet<RecurringRequest> RecurringRequests => Set<RecurringRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Username);
            entity.Property(x => x.Username).HasColumnName("username");
            entity.Property(x => x.Password).HasColumnName("password");
            entity.Property(x => x.PasswordHash).HasColumnName("passwordhash");
            entity.Property(x => x.PasswordSetAt).HasColumnName("passwordsetat");
            entity.Property(x => x.ForcePasswordChange).HasColumnName("forcepasswordchange");
            entity.Property(x => x.Role).HasColumnName("role");
            entity.Property(x => x.DisplayName).HasColumnName("displayname");
            entity.Property(x => x.Email).HasColumnName("email");
            entity.Property(x => x.Theme).HasColumnName("theme");
        });

        modelBuilder.Entity<Request>(entity =>
        {
            entity.ToTable("requests");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.EmployeeUsername).HasColumnName("employeeusername");
            entity.Property(x => x.EmployeeName).HasColumnName("employeename");
            entity.Property(x => x.Date).HasColumnName("date");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.DecisionBy).HasColumnName("decisionby");
            entity.Property(x => x.DecisionAt).HasColumnName("decisionat");
        });

        modelBuilder.Entity<RecurringRequest>(entity =>
        {
            entity.ToTable("recurringrequests");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.EmployeeUsername).HasColumnName("employeeusername");
            entity.Property(x => x.EmployeeName).HasColumnName("employeename");
            entity.Property(x => x.DayOfWeek).HasColumnName("dayofweek");
            entity.Property(x => x.DayName).HasColumnName("dayname");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.DecisionBy).HasColumnName("decisionby");
            entity.Property(x => x.DecisionAt).HasColumnName("decisionat");
        });
    }
}