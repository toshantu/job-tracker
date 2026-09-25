using JobTracker.Api.Data;
using JobTracker.Api.Dtos;
using JobTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Api.Endpoints;

public static class JobApplicationEndpoints
{
    public static void MapJobApplicationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/job-applications");

        group.MapGet("/", async (AppDbContext db, ApplicationStatus? status) =>
        {
            IQueryable<JobApplication> query = db.JobApplications
                .Include(j => j.InterviewStages)
                .Include(j => j.Documents);

            if (status is not null)
            {
                query = query.Where(j => j.Status == status);
            }

            var results = await query
                .OrderByDescending(j => j.DateApplied)
                .Select(j => ToResponse(j))
                .ToListAsync();
            
            return Results.Ok(results);            
        });

        group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        {
            var entity = await db.JobApplications
                .Include(j => j.InterviewStages)
                .Include(j => j.Documents)
                .FirstOrDefaultAsync(j => j.Id == id);

            return entity is null ? Results.NotFound() : Results.Ok(ToResponse(entity));
        });

        group.MapPost("/", async (JobApplicationCreateRequest request, AppDbContext db) =>
        {
            var entity = new JobApplication
            {
                Company = request.Company,
                RoleTitle = request.RoleTitle,
                Status = request.Status,
                DateApplied = request.DateApplied,
                Notes = request.Notes,
            };

            db.JobApplications.Add(entity);
            await db.SaveChangesAsync();

            return Results.Created($"/job-applications/{entity.Id}", ToResponse(entity));
        });

        group.MapPut("/{id:int}", async (int id, JobApplicationUpdateRequest request, AppDbContext db) =>
        {
            var entity = await db.JobApplications
                .Include(j => j.InterviewStages)
                .Include(j => j.Documents)
                .FirstOrDefaultAsync(j => j.Id == id);  

            if (entity is null)
            {
                return Results.NotFound();
            }

            entity.Company = request.Company;
            entity.RoleTitle = request.RoleTitle;
            entity.Status = request.Status;
            entity.DateApplied = request.DateApplied;
            entity.Notes = request.Notes;
            entity.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();

            return Results.Ok(ToResponse(entity));
        });

        group.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var entity = await db.JobApplications.FindAsync(id);

            if (entity is null)
            {
                return Results.NotFound();
            }

            db.JobApplications.Remove(entity);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });
        
    }

    private static JobApplicationResponse ToResponse(JobApplication j) => new(
        j.Id, j.Company, j.RoleTitle, j.Status, j.DateApplied, j.Notes, j.CreatedAt, j.UpdatedAt,
        j.InterviewStages.Select(s => new InterviewStageResponse(s.Id, s.StageName, s.ScheduledAt, s.Outcome, s.Notes)).ToList(),
        j.Documents.Select(d => new ApplicationDocumentResponse(d.Id, d.Type, d.Label, d.Notes)).ToList()
    );
    
}