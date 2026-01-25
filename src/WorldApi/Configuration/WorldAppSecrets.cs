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
    /// CloudFront distribution URL for serving chunks (optional).
    /// If provided, API will redirect to CloudFront instead of streaming from S3.
    /// Example: "https://d1234567890.cloudfront.net"
    /// </summary>
    [JsonPropertyName("cloudfrontUrl")]
    public string? CloudfrontUrl { get; set; }

    /// <summary>
    /// Flag indicating whether to use CloudFront for serving chunks.
    /// </summary>
    [JsonPropertyName("useCloudfront")]
    public string? UseCloudfront { get; set; }

    /// <summary>
    /// S3 bucket name used for world data storage. Moved from appsettings.json into app-managed secrets.
    /// </summary>
    [JsonPropertyName("s3BucketName")]
    public string? S3BucketName { get; set; }

    /// <summary>
    /// Flag to use local S3 (MinIO) instead of AWS S3. When true, CloudFront is disabled.
    /// Accepts string values "true"/"false" (case-insensitive).
    /// </summary>
    [JsonPropertyName("useLocalS3")]
    public string? UseLocalS3 { get; set; }

    /// <summary>
    /// Local S3 endpoint URL (e.g., "http://localhost:9000" for MinIO).
    /// Only used when UseLocalS3 is true.
    /// </summary>
    [JsonPropertyName("localS3Endpoint")]
    public string? LocalS3Endpoint { get; set; }

    /// <summary>
    /// Local S3 access key for MinIO. Only used when UseLocalS3 is true.
    /// </summary>
    [JsonPropertyName("localS3AccessKey")]
    public string? LocalS3AccessKey { get; set; }

    /// <summary>
    /// Local S3 secret key for MinIO. Only used when UseLocalS3 is true.
    /// </summary>
    [JsonPropertyName("localS3SecretKey")]
    public string? LocalS3SecretKey { get; set; }

    /// <summary>
    /// Local database username. Only used when UseLocalS3 is true.
    /// </summary>
    [JsonPropertyName("localDbUsername")]
    public string? LocalDbUsername { get; set; }

    /// <summary>
    /// Local database password. Only used when UseLocalS3 is true.
    /// </summary>
    [JsonPropertyName("localDbPassword")]
    public string? LocalDbPassword { get; set; }

    /// <summary>
    /// MapTiler API key for imagery tile requests.
    /// Used by ImageryTilesController to fetch tiles from MapTiler upstream.
    /// </summary>
    [JsonPropertyName("mapTilerApiKey")]
    public string? MapTilerApiKey { get; set; }

    /// <summary>
    /// Additional application settings can be added here without modifying infrastructure secrets.
    /// </summary>
}
