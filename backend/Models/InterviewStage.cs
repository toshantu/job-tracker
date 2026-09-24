namespace JobTracker.Api.Models;

public class InterviewStage
{
    public int Id { get; set; }
    public int JobApplicationId { get; set; }

    public JobApplication JobApplication { get; set; } = null!;
    public string StageName { get; set; } = string.Empty;
    public DateTimeOffset? ScheduledAt { get; set; }
    public InterviewOutcome Outcome { get; set; } = InterviewOutcome.Pending;
    public string? Notes { get; set; }
  
}

public enum InterviewOutcome
{
    Pending,
    Passed,
    Failed,
    Cancelled
}