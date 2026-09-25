using JobTracker.Api.Data;
using JobTracker.Api.Dtos;
using JobTracker.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Api.Endpoints;

public static class InterviewStageEndpoints
{
    public static void MapInterviewStageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/job-applications/{jobApplicationId:int}/interview-stages");

        group.MapPost("/", async (int jobApplicationId, InterviewStageCreateRequest request, AppDbContext db) =>
        {
            var jobApplication = await db.JobApplications.AnyAsync(j => j.Id == jobApplicationId);
            if (!jobApplication)
            {
                return Results.NotFound();
            }

            var entity = new InterviewStage
            {
                JobApplicationId = jobApplicationId,
                StageName = request.StageName,
                ScheduledAt = request.ScheduledAt,
                Outcome = request.Outcome,
                Notes = request.Notes
            };

            db.InterviewStages.Add(entity);
            await db.SaveChangesAsync();

            var response = new InterviewStageResponse(entity.Id, entity.StageName, entity.ScheduledAt, entity.Outcome, entity.Notes);
            return Results.Created($"/job-applications/{jobApplicationId}/interview-stages/{entity.Id}", response);
        });

        group.MapPut("/{stageId:int}", async (int jobApplicationId, int stageId, InterviewStageUpdateRequest request, AppDbContext db) =>
        {
            var entity = await db.InterviewStages.FirstOrDefaultAsync(s => s.Id == stageId && s.JobApplicationId == jobApplicationId);

            if (entity is null)
            {
                return Results.NotFound();
            }

            entity.StageName = request.StageName;
            entity.ScheduledAt = request.ScheduledAt;
            entity.Outcome = request.Outcome;
            entity.Notes = request.Notes;

            await db.SaveChangesAsync();

            var response = new InterviewStageResponse(entity.Id, entity.StageName, entity.ScheduledAt, entity.Outcome, entity.Notes);
            return Results.Ok(response);
        });

        group.MapDelete("/{stageId:int}", async (int jobApplicationId, int stageId, AppDbContext db) =>
        {
            var entity = await db.InterviewStages.FirstOrDefaultAsync(s => s.Id == stageId && s.JobApplicationId == jobApplicationId);

            if (entity is null)
            {
                return Results.NotFound();
            }

            db.InterviewStages.Remove(entity);
            await db.SaveChangesAsync();

            return Results.NoContent();
        });
    }
}