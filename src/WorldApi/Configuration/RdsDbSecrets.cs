namespace WorldApi.Configuration;

/// <summary>
/// POCO model representing the RDS-managed secret from AWS Secrets Manager.
/// This secret is created and rotated by Amazon RDS (read-only).
/// 
/// Contains only database credentials. Database host and port come from
/// the application-managed secret (WorldAppSecrets) instead.
/// </summary>
public class RdsDbSecrets
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
