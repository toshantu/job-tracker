using Microsoft.EntityFrameworkCore;
using JobTracker.Api.Models;

namespace JobTracker.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<JobApplication> JobApplications { get; set; } = null!;
    public DbSet<InterviewStage> InterviewStages { get; set; } = null!;
    public DbSet<ApplicationDocument> ApplicationDocuments { get; set; } = null!;


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().Property(e => e.Provider).HasConversion<string>();
        modelBuilder.Entity<User>().HasIndex(u => new { u.Provider, u.ProviderSubject }).IsUnique();


        modelBuilder.Entity<JobApplication>().Property(e => e.Status).HasConversion<string>();
        modelBuilder.Entity<InterviewStage>().Property(e => e.Outcome).HasConversion<string>();
        modelBuilder.Entity<ApplicationDocument>().Property(e => e.Type).HasConversion<string>();
    
    }

}