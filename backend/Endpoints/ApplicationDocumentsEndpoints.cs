using JobTracker.Api.Data;
using JobTracker.Api.Dtos; 
using JobTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Api.Endpoints;

public static class ApplicationDocumentsEndpoints
{
    public static void MapApplicationDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/job-applications/{jobApplicationId:int}/documents");

        group.MapPost("/", async (int jobApplicationId, ApplicationDocumentCreateRequest request, AppDbContext db) =>
        {
            var jobApplication = await db.JobApplications.AnyAsync(j => j.Id == jobApplicationId);
            if (!jobApplication)
            {
                return Results.NotFound();
            }

            var entity = new ApplicationDocument
            {
                JobApplicationId = jobApplicationId,
                Type = request.Type,
                Label = request.Label,
                Notes = request.Notes
            };

            db.ApplicationDocuments.Add(entity);
            await db.SaveChangesAsync();

            var response = new ApplicationDocumentResponse(entity.Id, entity.Type, entity.Label, entity.Notes);
            return Results.Created($"/job-applications/{jobApplicationId}/documents/{entity.Id}", response);
        });

        group.MapPut("/{docId:int}", async (int jobApplicationId, int docId, ApplicationDocumentUpdateRequest request, AppDbContext db) =>
        {
            var entity = await db.ApplicationDocuments.FirstOrDefaultAsync(d => d.Id == docId && d.JobApplicationId == jobApplicationId);

            if (entity is null)
            {
                return Results.NotFound();
            }

            entity.Type = request.Type;
            entity.Label = request.Label;
            entity.Notes = request.Notes;

            await db.SaveChangesAsync();

            var response = new ApplicationDocumentResponse(entity.Id, entity.Type, entity.Label, entity.Notes);
            return Results.Ok(response);
        });

        group.MapDelete("/{docId:int}", async (int jobApplicationId, int docId, AppDbContext db) =>
        {
            var entity = await db.ApplicationDocuments.FirstOrDefaultAsync(d => d.Id == docId && d.JobApplicationId == jobApplicationId);

            if (entity is null)
            {
                return Results.NotFound();
            }

            db.ApplicationDocuments.Remove(entity);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });
    }
}