using System.ComponentModel.DataAnnotations;
using JobTracker.Api.Models;

namespace JobTracker.Api.Dtos;

public record JobApplicationResponse(
    int Id,
    string Company,
    string RoleTitle,
    ApplicationStatus Status,
    DateOnly DateApplied,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<InterviewStageResponse> InterviewStages,
    List<ApplicationDocumentResponse> Documents
);

public record InterviewStageResponse(
    int Id,
    string StageName,
    DateTimeOffset? ScheduledAt,
    InterviewOutcome Outcome,
    string? Notes
);

public record ApplicationDocumentResponse(
    int Id,
    DocumentType Type,
    string Label,
    string? Notes
);

public record JobApplicationCreateRequest(
    [Required, StringLength(200)] string Company,
    [Required, StringLength(200)] string RoleTitle,
    ApplicationStatus Status,
    DateOnly DateApplied,
    string? Notes
);

public record JobApplicationUpdateRequest(
    [Required, StringLength(200)] string Company,
    [Required, StringLength(200)] string RoleTitle,
    ApplicationStatus Status,
    DateOnly DateApplied,
    string? Notes
);