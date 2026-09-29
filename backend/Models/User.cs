namespace JobTracker.Api.Models;

public class User
{
    public int Id { get; set; }
    public AuthProvider Provider { get; set; }
    public string ProviderSubject { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public ICollection<JobApplication> JobApplications { get; set; } = new List<JobApplication>();
}

public enum AuthProvider
{
    Google,  
    GitHub
}