using WorldApi.World;
using WorldApi.Configuration;
using Amazon.S3;
using Amazon.SecretsManager;
using Microsoft.Extensions.Options;

// Load .env file for local development
var envFile = FindEnvFile();
if (envFile != null && File.Exists(envFile))
{
    foreach (var line in File.ReadAllLines(envFile))
    {
        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2)
                Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
        }
    }
}

var builder = WebApplication.CreateBuilder(args);

// Services

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configuration
builder.Services.Configure<WorldConfig>(builder.Configuration.GetSection("World"));

// AWS Clients
builder.Services.AddSingleton<IAmazonS3>(sp => new AmazonS3Client());
builder.Services.AddSingleton<IAmazonSecretsManager>(sp => new AmazonSecretsManagerClient());

// Secrets Manager Service
builder.Services.AddSingleton<SecretsManagerService>();

// Load secrets from AWS Secrets Manager at startup
var awsRegion = builder.Configuration["AWS_REGION"] ?? Environment.GetEnvironmentVariable("AWS_REGION");
var rdsSecretArn = builder.Configuration["AWS_RDS_SECRET_ARN"] ?? Environment.GetEnvironmentVariable("AWS_RDS_SECRET_ARN");
var appSecretArn = builder.Configuration["AWS_APP_SECRET_ARN"] ?? Environment.GetEnvironmentVariable("AWS_APP_SECRET_ARN");

if (string.IsNullOrEmpty(rdsSecretArn))
{
    throw new InvalidOperationException("AWS_RDS_SECRET_ARN environment variable is required");
}

if (string.IsNullOrEmpty(appSecretArn))
{
    throw new InvalidOperationException("AWS_APP_SECRET_ARN environment variable is required");
}

// Build a temporary service provider to resolve SecretsManagerService
// Warning ASP0000 is expected here - we need to fetch secrets before the main DI container is built
// This is a common pattern when loading configuration from external sources at startup
#pragma warning disable ASP0000
using var tempServiceProvider = builder.Services.BuildServiceProvider();
var secretsManager = tempServiceProvider.GetRequiredService<SecretsManagerService>();

// Fetch both secrets concurrently for better startup performance
var rdsSecretsTask = secretsManager.GetRdsDbSecretsAsync(rdsSecretArn);
var appSecretsTask = secretsManager.GetWorldAppSecretsAsync(appSecretArn);
await Task.WhenAll(rdsSecretsTask, appSecretsTask);

var rdsSecrets = await rdsSecretsTask;
var appSecrets = await appSecretsTask;
#pragma warning restore ASP0000

// Build connection string using RDS credentials + application secrets (database, host, port)
var connectionString = $"Host={appSecrets.DbHost};Port={appSecrets.DbPort};Database={appSecrets.Database};Username={rdsSecrets.Username};Password={rdsSecrets.Password}";

// Log successful secret retrieval (without exposing sensitive data)
var logger = tempServiceProvider.GetRequiredService<ILogger<Program>>();
// logger.LogInformation("Successfully loaded secrets from AWS Secrets Manager");
// logger.LogInformation("RDS secret: username={Username}", rdsSecrets.Username);
// logger.LogInformation("App secret: database={Database}, host={Host}, port={Port}, worldVersion={WorldVersion}, cloudfrontUrl={CloudfrontUrl}", appSecrets.Database, appSecrets.DbHost, appSecrets.DbPort, appSecrets.WorldVersion, appSecrets.CloudfrontUrl ?? "(not configured)");

// Register application secrets as singleton for lifetime of the app
builder.Services.AddSingleton(appSecrets);
builder.Services.AddSingleton<IOptions<WorldAppSecrets>>(new OptionsWrapper<WorldAppSecrets>(appSecrets));

// World services
builder.Services.AddSingleton<WorldCoordinateService>();
builder.Services.AddSingleton<HgtTileCache>();
builder.Services.AddSingleton<DemTileIndex>();

// Factory for services that need bucket name
builder.Services.AddSingleton<DemTileIndexBuilder>(sp =>
{
    var s3Client = sp.GetRequiredService<IAmazonS3>();
    var bucketName = builder.Configuration["AWS:S3:BucketName"] ?? throw new InvalidOperationException("AWS:S3:BucketName not configured");
    return new DemTileIndexBuilder(s3Client, bucketName);
});

builder.Services.AddSingleton<HgtTileLoader>(sp =>
{
    var s3Client = sp.GetRequiredService<IAmazonS3>();
    var bucketName = builder.Configuration["AWS:S3:BucketName"] ?? throw new InvalidOperationException("AWS:S3:BucketName not configured");
    return new HgtTileLoader(s3Client, bucketName);
});

builder.Services.AddSingleton<ITerrainChunkReader>(sp =>
{
    var s3Client = sp.GetRequiredService<IAmazonS3>();
    var bucketName = builder.Configuration["AWS:S3:BucketName"] ?? throw new InvalidOperationException("AWS:S3:BucketName not configured");
    return new TerrainChunkReader(s3Client, bucketName);
});

builder.Services.AddSingleton<TerrainChunkWriter>(sp =>
{
    var s3Client = sp.GetRequiredService<IAmazonS3>();
    var bucketName = builder.Configuration["AWS:S3:BucketName"] ?? throw new InvalidOperationException("AWS:S3:BucketName not configured");
    var config = sp.GetRequiredService<IOptions<WorldConfig>>();
    var logger = sp.GetRequiredService<ILogger<TerrainChunkWriter>>();
    return new TerrainChunkWriter(s3Client, bucketName, config, logger);
});

builder.Services.AddSingleton<TerrainChunkGenerator>(sp =>
{
    var coordinateService = sp.GetRequiredService<WorldCoordinateService>();
    var tileIndex = sp.GetRequiredService<DemTileIndex>();
    var tileCache = sp.GetRequiredService<HgtTileCache>();
    var tileLoader = sp.GetRequiredService<HgtTileLoader>();
    var config = sp.GetRequiredService<IOptions<WorldConfig>>();
    var logger = sp.GetRequiredService<ILogger<TerrainChunkGenerator>>();
    return new TerrainChunkGenerator(coordinateService, tileIndex, tileCache, tileLoader, config, logger);
});

builder.Services.AddScoped<WorldChunkRepository>(sp =>
{
    // Use the connection string built from AWS Secrets Manager
    return new WorldChunkRepository(connectionString);
});

// Register terrain chunk coordinator (orchestration only - no S3 dependencies)
builder.Services.AddScoped<ITerrainChunkCoordinator, TerrainChunkCoordinator>();

// Register hosted service to populate DEM tile index at startup
builder.Services.AddHostedService<DemTileIndexInitializer>();

// ---- CORS (DEV ONLY) ----
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
    {
        policy
            .WithOrigins("http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Middleware

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseRouting();

app.UseCors("DevCors");

app.UseAuthorization();

// Enable attribute-routed controllers
app.MapControllers();

app.Run();

/// <summary>
/// Search upward from current directory to find .env file
/// </summary>
static string? FindEnvFile()
{
    var currentDir = Directory.GetCurrentDirectory();
    
    // Search up to 5 levels up
    for (int i = 0; i < 5; i++)
    {
        var envPath = Path.Combine(currentDir, ".env");
        if (File.Exists(envPath))
            return envPath;
        
        var parentDir = Directory.GetParent(currentDir)?.FullName;
        if (parentDir == null)
            break;
        
        currentDir = parentDir;
    }
    
    return null;
}
