using JobTracker.Api.Auth;
using JobTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin")
            .RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole(AppRoles.Admin));

        group.MapGet("/stats", async (AppDbContext db) =>
        {
            var totalUsers = await db.Users.CountAsync();
            var usersByProvider = await db.Users
                .GroupBy(u => u.Provider)
                .Select(g => new { Provider = g.Key, Count = g.Count() })
                .ToListAsync();

            var totalApplications = await db.JobApplications.CountAsync();
            var applicationsByStatus = await db.JobApplications
                .GroupBy(j => j.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            return Results.Ok(new
            {
                users = new
                {
                    total = totalUsers,
                    byProvider = usersByProvider.ToDictionary(x => x.Provider.ToString(), x => x.Count),
                },
                jobApplications = new
                {
                    total = totalApplications,
                    byStatus = applicationsByStatus.ToDictionary(x => x.Status.ToString(), x => x.Count),
                },
            });
        });
    }
}