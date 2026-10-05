using System.Security.Claims;
using JobTracker.Api.Auth;
using JobTracker.Api.Data;
using JobTracker.Api.Dtos;
using JobTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Api.Endpoints;

public static class ApplicationDocumentsEndpoints
{
    public static void MapApplicationDocumentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/job-applications/{jobApplicationId:int}/documents").RequireAuthorization();

        group.MapPost("/", async (int jobApplicationId, ApplicationDocumentCreateRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.GetUserId();

            var ownsParent = await db.JobApplications.AnyAsync(j => j.Id == jobApplicationId && j.UserId == userId);
            if (!ownsParent)
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

        group.MapPut("/{docId:int}", async (int jobApplicationId, int docId, ApplicationDocumentUpdateRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.GetUserId();

            var entity = await db.ApplicationDocuments.FirstOrDefaultAsync(d =>
                d.Id == docId
                && d.JobApplicationId == jobApplicationId
                && d.JobApplication.UserId == userId);

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

        group.MapDelete("/{docId:int}", async (int jobApplicationId, int docId, ClaimsPrincipal user, AppDbContext db) =>
        {
            var userId = user.GetUserId();

            var entity = await db.ApplicationDocuments.FirstOrDefaultAsync(d =>
                d.Id == docId
                && d.JobApplicationId == jobApplicationId
                && d.JobApplication.UserId == userId);

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