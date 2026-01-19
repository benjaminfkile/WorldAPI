using System.Text.Json.Serialization;

namespace WorldApi.Configuration;

/// <summary>
/// POCO model representing the application-managed secret from AWS Secrets Manager.
/// This secret is created and managed by the World API application.
/// 
/// Contains application-specific configuration that should not be environment-specific
/// in code (database name, world version, database host, database port, etc.).
/// </summary>
public class WorldAppSecrets
{
    /// <summary>
    /// Database name for the World API.
    /// </summary>
    public string Database { get; set; } = "world";

    /// <summary>
    /// Database host (RDS endpoint or localhost).
    /// </summary>
    public string DbHost { get; set; } = "localhost";

    /// <summary>
    /// Database port.
    /// </summary>
    public string DbPort { get; set; } = "5432";

    /// <summary>
    /// World version identifier (e.g., "world-v1").
    /// Used for versioning terrain data and world configuration.
    /// </summary>
    public string WorldVersion { get; set; } = "world-v1";

    /// <summary>
    /// CloudFront distribution URL for serving chunks (optional).
    /// If provided, API will redirect to CloudFront instead of streaming from S3.
    /// Example: "https://d1234567890.cloudfront.net"
    /// </summary>
    [JsonPropertyName("cloudfrontUrl")]
    public string? CloudfrontUrl { get; set; }

    /// <summary>
    /// Additional application settings can be added here without modifying infrastructure secrets.
    /// </summary>
}
