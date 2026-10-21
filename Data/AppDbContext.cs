using Microsoft.EntityFrameworkCore;
using Africanideasfaceapi.Models;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Worker> Workers { get; set; }
    public DbSet<LeaveRequest> LeaveRequests { get; set; }
    public DbSet<LeaveBalance> LeaveBalances { get; set; }
        public DbSet<IdCardVerification> IdCardVerifications { get; set; }
        public DbSet<Consent> Consents { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<FaceLog> FaceLogs { get; set; }
        public DbSet<Attendance> Attendance { get; set; }
    public DbSet<WhatsAppSession> WhatsAppSessions { get; set; }
        public DbSet<Branch> Branches { get; set; }
        public DbSet<Absence> Absences { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<AttendanceCorrection> CorrectionRequests { get; set; }
        public DbSet<SystemSettings> SystemSettings { get; set; }
        public DbSet<RemoteWorkRequest> RemoteWorkRequests { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Configure Cosmos DB container and partition key for Users
            modelBuilder.Entity<User>()
                .ToContainer("Users")
                .HasPartitionKey(u => u.Id)
                .HasNoDiscriminator();

            modelBuilder.Entity<User>()
                .Property(u => u.Id)
                .ToJsonProperty("id");

            modelBuilder.Entity<User>()
                .Property(u => u.HybridWorkDays)
                .ToJsonProperty("hybridWorkDays");


            // Configure Workers container
            modelBuilder.Entity<Worker>()
                .ToContainer("Workers")
                .HasPartitionKey(p => p.Id)
                .HasNoDiscriminator()
                .Property(p => p.Id)
                .ToJsonProperty("id");
            
            // Configure ID Card Verifications container
            modelBuilder.Entity<IdCardVerification>()
                .ToContainer("IdCardVerifications")
                .HasPartitionKey(v => v.Id)
                .HasNoDiscriminator()
                .Property(v => v.Id)
                .ToJsonProperty("id");
            
            // Configure Consents container
            modelBuilder.Entity<Consent>()
                .ToContainer("Consents")
                .HasPartitionKey(c => c.Id)
                .HasNoDiscriminator()
                .Property(c => c.Id)
                .ToJsonProperty("id");
            
            // Configure Audit Logs container
            modelBuilder.Entity<AuditLog>()
                .ToContainer("AuditLogs")
                .HasPartitionKey(a => a.Id)
                .HasNoDiscriminator()
                .Property(a => a.Id)
                .ToJsonProperty("id");

            // Configure FaceLogs container
            modelBuilder.Entity<FaceLog>()
                .ToContainer("FaceLogs")
                .HasPartitionKey(f => f.Id)
                .HasNoDiscriminator()
                .Property(f => f.Id)
                .ToJsonProperty("id");

            // Attendance container partition key path is /id (immutable after EnsureCreated).
            modelBuilder.Entity<Attendance>()
                .ToContainer("Attendance")
                .HasPartitionKey(a => a.Id)
                .HasNoDiscriminator()
                .Property(a => a.Id)
                .ToJsonProperty("id");

            // Configure Branches container
            modelBuilder.Entity<Branch>()
                .ToContainer("Branches")
                .HasPartitionKey(b => b.Id)
                .HasNoDiscriminator()
                .Property(b => b.Id)
                .ToJsonProperty("id");

            // Configure Absences container
            modelBuilder.Entity<Absence>()
                .ToContainer("Absences")
                .HasPartitionKey(a => a.Id)
                .HasNoDiscriminator()
                .Property(a => a.Id)
                .ToJsonProperty("id");

            // Configure Notifications container
            modelBuilder.Entity<Notification>()
                .ToContainer("Notifications")
                .HasPartitionKey(n => n.Id)
                .HasNoDiscriminator()
                .Property(n => n.Id)
                .ToJsonProperty("id");

            // Configure CorrectionRequests container
            modelBuilder.Entity<AttendanceCorrection>()
                .ToContainer("CorrectionRequests")
                .HasPartitionKey(c => c.Id)
                .HasNoDiscriminator()
                .Property(c => c.Id)
                .ToJsonProperty("id");

            modelBuilder.Entity<LeaveRequest>()
                .ToContainer("LeaveRequests")
                .HasPartitionKey(l => l.Id)
                .HasNoDiscriminator()
                .Property(l => l.Id)
                .ToJsonProperty("id");

            modelBuilder.Entity<LeaveBalance>()
                .ToContainer("LeaveBalances")
                .HasPartitionKey(lb => lb.Id)
                .HasNoDiscriminator()
                .Property(lb => lb.Id)
                .ToJsonProperty("id");

            modelBuilder.Entity<SystemSettings>()
                .ToContainer("SystemSettings")
                .HasPartitionKey(s => s.Id)
                .HasNoDiscriminator()
                .Property(s => s.Id)
                .ToJsonProperty("id");

            modelBuilder.Entity<RemoteWorkRequest>()
                .ToContainer("RemoteWorkRequests")
                .HasPartitionKey(r => r.Id)
                .HasNoDiscriminator()
                .Property(r => r.Id)
                .ToJsonProperty("id");
            
            // Note: Cosmos DB doesn't support unique indexes like SQL databases
            // Email uniqueness will be enforced at the application level in services
        }
    }
}
