namespace JobTracker.Api.Models;

public class JobApplication
{
    public int Id { get; set; }
    public string Company { get; set; } = string.Empty;
    public string RoleTitle { get; set; } = string.Empty;
    public ApplicationStatus Status { get; set; }= ApplicationStatus.Wishlist;
    public DateOnly DateApplied { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<InterviewStage> InterviewStages { get; set; } = new List<InterviewStage>();
    public ICollection<ApplicationDocument> Documents { get; set; } = new List<ApplicationDocument>();

}

public enum ApplicationStatus
{
    Wishlist,
    Applied,
    Interviewing,
    Offer,
    Accepted,
    Rejected,
    Withdrawn
}