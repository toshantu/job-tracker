namespace JobTracker.Api.Models;

public class ApplicationDocument
{
    public int Id { get; set; }
    public int JobApplicationId { get; set; }

    public JobApplication JobApplication { get; set; } = null!;
    public DocumentType Type { get; set; }

    public string Label { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public enum DocumentType
{
    Cv,
    CoverLetter
    
}