using Microsoft.EntityFrameworkCore;

namespace JobTracker.Api.data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

}