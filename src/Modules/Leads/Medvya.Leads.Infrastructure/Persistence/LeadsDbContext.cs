using Medvya.Leads.Domain;
using Microsoft.EntityFrameworkCore;

namespace Medvya.Leads.Infrastructure.Persistence;

/// <summary>Sales leads from the public site. Not clinic data, so no tenant filter; the module role may only add and read rows.</summary>
public sealed class LeadsDbContext(DbContextOptions<LeadsDbContext> options) : DbContext(options)
{
    public DbSet<DemoRequest> DemoRequests => Set<DemoRequest>();
    public DbSet<WaitlistEntry> Waitlist => Set<WaitlistEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("leads");
        modelBuilder.Entity<DemoRequest>(e =>
        {
            e.ToTable("DemoRequests");
            e.Property(x => x.Name).HasMaxLength(DemoRequest.NameMax);
            e.Property(x => x.Organisation).HasMaxLength(DemoRequest.OrganisationMax);
            e.Property(x => x.Role).HasMaxLength(DemoRequest.RoleMax);
            e.Property(x => x.Country).HasMaxLength(5).IsUnicode(false);
            e.Property(x => x.Email).HasMaxLength(DemoRequest.EmailMax);
            e.Property(x => x.Phone).HasMaxLength(DemoRequest.PhoneMax).IsUnicode(false);
            e.Property(x => x.Message).HasMaxLength(DemoRequest.MessageMax);
            e.Property(x => x.NoticeVersion).HasMaxLength(20).IsUnicode(false);
            e.HasIndex(x => x.ReceivedAt);
        });
        modelBuilder.Entity<WaitlistEntry>(e =>
        {
            e.ToTable("Waitlist");
            e.Property(x => x.Name).HasMaxLength(WaitlistEntry.NameMax);
            e.Property(x => x.Country).HasMaxLength(2).IsUnicode(false);
            e.Property(x => x.Email).HasMaxLength(LeadRules.EmailMax);
            e.Property(x => x.Phone).HasMaxLength(WaitlistEntry.PhoneMax).IsUnicode(false);
            e.Property(x => x.NoticeVersion).HasMaxLength(20).IsUnicode(false);
            e.HasIndex(x => new { x.Country, x.ReceivedAt });
        });
    }
}
