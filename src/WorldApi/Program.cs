using WorldApi.World.Config;
using WorldApi.World.Coordinates;
using WorldApi.World.Dem;
using WorldApi.World.Chunks;
using WorldApi.Configuration;
using Amazon.S3;
using Amazon.SecretsManager;
using Microsoft.Extensions.Options;
using Npgsql;

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
// Placeholder - will be replaced after secrets are loaded
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

// Fetch app secrets first to check if we're using local mode
var appSecrets = await secretsManager.GetWorldAppSecretsAsync(appSecretArn);
var useLocalS3 = bool.TryParse(appSecrets.UseLocalS3, out var parsed) && parsed;

string connectionString;

if (useLocalS3)
{
    // Local mode: use local DB credentials from app secret
    var localUsername = appSecrets.LocalDbUsername ?? "postgres";
    var localPassword = appSecrets.LocalDbPassword ?? "postgres";
    connectionString = $"Host={appSecrets.DbHost};Port={appSecrets.DbPort};Database={appSecrets.Database};Username={localUsername};Password={localPassword}";
    
    var logger = tempServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Using local database credentials (username: {Username})", localUsername);
}
else
{
    // Production mode: fetch RDS secret for credentials
    var rdsSecrets = await secretsManager.GetRdsDbSecretsAsync(rdsSecretArn);
    connectionString = $"Host={appSecrets.DbHost};Port={appSecrets.DbPort};Database={appSecrets.Database};Username={rdsSecrets.Username};Password={rdsSecrets.Password}";
}

#pragma warning restore ASP0000

// Register application secrets as singleton for lifetime of the app
builder.Services.AddSingleton(appSecrets);
builder.Services.AddSingleton<IOptions<WorldAppSecrets>>(new OptionsWrapper<WorldAppSecrets>(appSecrets));

// Register NpgsqlDataSource as singleton with connection pooling configuration
// This eliminates connection storms by maintaining a shared pool (max 20 connections)
// Timeout: 15 seconds for acquiring a connection from pool
// CommandTimeout: 30 seconds for SQL commands
builder.Services.AddSingleton<NpgsqlDataSource>(sp =>
{
    var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString)
    {
        MaxPoolSize = 20,  // Limit concurrent connections to prevent storms
        Timeout = 15       // Acquisition timeout in seconds
    };
    
    return new NpgsqlDataSourceBuilder(connectionStringBuilder.ConnectionString).Build();
});

// Update WorldVersionService with NpgsqlDataSource
builder.Services.AddSingleton<IWorldVersionService>(sp =>
{
    var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
    return new WorldVersionService(dataSource);
});

// Configure S3 client based on secrets (before anchor chunk generator)
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    // Parse UseLocalS3 string to bool (accepts "true"/"false", case-insensitive)
    var useLocalS3 = bool.TryParse(appSecrets.UseLocalS3, out var parsed) && parsed;
    
    if (useLocalS3)
    {
        // Configure client for local MinIO (or compatible) endpoint
        var endpoint = appSecrets.LocalS3Endpoint ?? "http://localhost:9000";
        
        // Ensure endpoint has a scheme (http:// or https://)
        if (!endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
            !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = $"http://{endpoint}";
        }
        
        var s3Config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true
        };

        if (!string.IsNullOrEmpty(appSecrets.LocalS3AccessKey) && !string.IsNullOrEmpty(appSecrets.LocalS3SecretKey))
        {
            var creds = new Amazon.Runtime.BasicAWSCredentials(appSecrets.LocalS3AccessKey, appSecrets.LocalS3SecretKey);
            return new AmazonS3Client(creds, s3Config);
        }

        return new AmazonS3Client(s3Config);
    }

    // Default AWS S3 client (uses EC2 role or environment credentials)
    return new AmazonS3Client();
});

// Register HttpClient for tile requests (imagery, DEM, etc)
builder.Services.AddHttpClient();

// World services (required for AnchorChunkGenerator)
builder.Services.AddSingleton<WorldCoordinateService>();
builder.Services.AddSingleton<HgtTileCache>();
builder.Services.AddSingleton<DemTileIndex>();

// Register AnchorChunkGenerator before startup scope (needed for anchor generation)
builder.Services.AddSingleton<AnchorChunkGenerator>();

// Load active world versions from PostgreSQL at startup
// This happens BEFORE the DI container is finalized, ensuring cache is ready for requests
#pragma warning disable ASP0000
using (var preStartScope = builder.Services.BuildServiceProvider().CreateScope())
{
    var dataSource = preStartScope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
    var logger = preStartScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    logger.LogInformation("🚀 Loading active world versions from PostgreSQL at startup...");
    
    var activeVersions = await LoadActiveWorldVersionsFromDatabaseAsync(dataSource, logger);
    
    if (activeVersions.Count == 0)
    {
        var errorMsg = "❌ STARTUP FAILURE: No active world versions found in database. " +
            "At least one world version must have is_active=true. " +
            "Check your database configuration.";
        logger.LogCritical(errorMsg);
        throw new InvalidOperationException(errorMsg);
    }

    logger.LogInformation("✓ Successfully loaded {Count} active world version(s) at startup", activeVersions.Count);

    // Register the populated cache as singleton
    builder.Services.AddSingleton<IWorldVersionCache>(sp =>
    {
        var cacheLogger = sp.GetRequiredService<ILogger<WorldVersionCache>>();
        return new WorldVersionCache(activeVersions, cacheLogger);
    });

    // Generate anchor chunks for world versions that don't have any chunks yet
    logger.LogInformation("🔧 Checking if anchor chunks need to be generated...");
    
    try
    {
        var repository = new WorldChunkRepository(dataSource);
        var anchorGenerator = preStartScope.ServiceProvider.GetRequiredService<AnchorChunkGenerator>();
        var s3Client = preStartScope.ServiceProvider.GetRequiredService<IAmazonS3>();
        var appSecretsOpts = preStartScope.ServiceProvider.GetRequiredService<IOptions<WorldAppSecrets>>();
        var startupAppSecrets = appSecretsOpts.Value;
        var loggerFactory = preStartScope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var chunkWriterLogger = loggerFactory.CreateLogger<TerrainChunkWriter>();
        
        var bucketName = startupAppSecrets.S3BucketName ?? throw new InvalidOperationException("S3 bucket name not configured in app secrets (s3BucketName)");
        var chunkWriter = new TerrainChunkWriter(s3Client, bucketName, chunkWriterLogger);

        foreach (var version in activeVersions)
        {
            // Check if any chunks exist for this world version
            if (await repository.AnyChunksExistAsync(version.Version))
            {
                logger.LogInformation("✓ World version '{Version}' already has chunks, skipping anchor generation", version.Version);
                continue;
            }

            logger.LogInformation("📍 Generating anchor chunk for world version '{Version}'...", version.Version);

            // Generate the anchor chunk
            var anchorChunk = anchorGenerator.GenerateAnchorChunk();
            
            // Write chunk to S3
            var s3Key = anchorGenerator.GetAnchorChunkS3Key(version.Version);
            var uploadResult = await chunkWriter.WriteAsync(anchorChunk, s3Key);
            
            // Insert chunk metadata into database
            await repository.UpsertReadyAsync(
                anchorChunk.ChunkX,
                anchorChunk.ChunkZ,
                anchorGenerator.GetAnchorLayer(),
                anchorChunk.Resolution,
                version.Version,
                s3Key,
                uploadResult.Checksum);
            
            logger.LogInformation("✓ Anchor chunk persisted for world version '{Version}': S3Key={S3Key}", version.Version, s3Key);
        }

        logger.LogInformation("✓ Anchor chunk initialization complete");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "⚠ Anchor chunk generation failed - this may indicate a configuration issue");
        throw;
    }
}
#pragma warning restore ASP0000

// HttpClient for PublicSrtmClient
builder.Services.AddSingleton<PublicSrtmClient>(sp =>
{
    var httpClient = new HttpClient();
    return new PublicSrtmClient(httpClient);
});

// Factory for services that need bucket name
builder.Services.AddSingleton<DemTileIndexBuilder>(sp =>
{
    var s3Client = sp.GetRequiredService<IAmazonS3>();
    var appSecrets = sp.GetRequiredService<IOptions<WorldAppSecrets>>().Value;
    var bucketName = appSecrets.S3BucketName ?? throw new InvalidOperationException("S3 bucket name not configured in app secrets (s3BucketName)");
    return new DemTileIndexBuilder(s3Client, bucketName);
});

builder.Services.AddSingleton<HgtTileLoader>(sp =>
{
    var s3Client = sp.GetRequiredService<IAmazonS3>();
    var appSecrets = sp.GetRequiredService<IOptions<WorldAppSecrets>>().Value;
    var bucketName = appSecrets.S3BucketName ?? throw new InvalidOperationException("S3 bucket name not configured in app secrets (s3BucketName)");
    return new HgtTileLoader(s3Client, bucketName);
});

builder.Services.AddSingleton<ITerrainChunkReader>(sp =>
{
    var s3Client = sp.GetRequiredService<IAmazonS3>();
    var appSecrets = sp.GetRequiredService<IOptions<WorldAppSecrets>>().Value;
    var bucketName = appSecrets.S3BucketName ?? throw new InvalidOperationException("S3 bucket name not configured in app secrets (s3BucketName)");
    return new TerrainChunkReader(s3Client, bucketName);
});

builder.Services.AddSingleton<TerrainChunkWriter>(sp =>
{
    var s3Client = sp.GetRequiredService<IAmazonS3>();
    var appSecrets = sp.GetRequiredService<IOptions<WorldAppSecrets>>().Value;
    var bucketName = appSecrets.S3BucketName ?? throw new InvalidOperationException("S3 bucket name not configured in app secrets (s3BucketName)");
    var logger = sp.GetRequiredService<ILogger<TerrainChunkWriter>>();
    return new TerrainChunkWriter(s3Client, bucketName, logger);
});

// DEM lazy fetch services
builder.Services.AddSingleton<DemTileWriter>(sp =>
{
    var s3Client = sp.GetRequiredService<IAmazonS3>();
    var appSecrets = sp.GetRequiredService<IOptions<WorldAppSecrets>>().Value;
    var bucketName = appSecrets.S3BucketName ?? throw new InvalidOperationException("S3 bucket name not configured in app secrets (s3BucketName)");
    return new DemTileWriter(s3Client, bucketName);
});

builder.Services.AddSingleton<DemTileResolver>(sp =>
{
    var index = sp.GetRequiredService<DemTileIndex>();
    var publicClient = sp.GetRequiredService<PublicSrtmClient>();
    var writer = sp.GetRequiredService<DemTileWriter>();
    return new DemTileResolver(index, publicClient, writer);
});

builder.Services.AddSingleton<TerrainChunkGenerator>(sp =>
{
    var coordinateService = sp.GetRequiredService<WorldCoordinateService>();
    var resolver = sp.GetRequiredService<DemTileResolver>();
    var tileCache = sp.GetRequiredService<HgtTileCache>();
    var tileLoader = sp.GetRequiredService<HgtTileLoader>();
    var config = sp.GetRequiredService<IOptions<WorldConfig>>();
    var logger = sp.GetRequiredService<ILogger<TerrainChunkGenerator>>();
    return new TerrainChunkGenerator(coordinateService, resolver, tileCache, tileLoader, config, logger);
});

builder.Services.AddScoped<WorldChunkRepository>(sp =>
{
    // Inject NpgsqlDataSource (eliminates direct connection creation)
    var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
    return new WorldChunkRepository(dataSource);
});

// ============================================================================
// DEM Readiness Gating Services
// ============================================================================

// Register DEM tile repository for database access
builder.Services.AddSingleton<DemTileRepository>(sp =>
{
    var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
    return new DemTileRepository(dataSource);
});

// Register DEM status service for querying and managing tile readiness
// Uses a callback to enqueue DEM downloads when tiles transition to 'missing'
builder.Services.AddSingleton<DemStatusService>(sp =>
{
    var repository = sp.GetRequiredService<DemTileRepository>();
    var logger = sp.GetRequiredService<ILogger<DemStatusService>>();
    
    // Callback to enqueue DEM download (fire-and-forget)
    // This will be invoked when a new tile transitions to 'missing'
    Action<string, string>? onTileMissingCallback = (worldVersion, tileKey) =>
    {
        logger.LogInformation(
            "DEM tile enqueued for download: world={WorldVersion}, tileKey={TileKey}",
            worldVersion, tileKey);
        // Background worker will poll for missing tiles and process them
    };
    
    return new DemStatusService(repository, logger, onTileMissingCallback);
});

// Register DEM download worker as hosted service
// Polls database for missing/downloading tiles and processes them in background
builder.Services.AddHostedService<DemDownloadWorker>(sp =>
{
    var demRepository = sp.GetRequiredService<DemTileRepository>();
    var publicClient = sp.GetRequiredService<PublicSrtmClient>();
    var demWriter = sp.GetRequiredService<DemTileWriter>();
    var demIndex = sp.GetRequiredService<DemTileIndex>();
    var versionCache = sp.GetRequiredService<IWorldVersionCache>();
    var logger = sp.GetRequiredService<ILogger<DemDownloadWorker>>();
    return new DemDownloadWorker(demRepository, publicClient, demWriter, demIndex, versionCache, logger);
});

// Register terrain chunk coordinator (orchestration only - no S3 dependencies)
// SemaphoreSlim(3) limits concurrent database writes to 3 to prevent connection exhaustion
builder.Services.AddScoped<ITerrainChunkCoordinator>(sp =>
{
    var repository = sp.GetRequiredService<WorldChunkRepository>();
    var generator = sp.GetRequiredService<TerrainChunkGenerator>();
    var writer = sp.GetRequiredService<TerrainChunkWriter>();
    var demStatusService = sp.GetRequiredService<DemStatusService>();
    var logger = sp.GetRequiredService<ILogger<TerrainChunkCoordinator>>();
    var dbWriteSemaphore = new SemaphoreSlim(3, 3);  // Max 3 concurrent DB writes
    return new TerrainChunkCoordinator(repository, generator, writer, demStatusService, logger, dbWriteSemaphore);
});

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
/// Load active world versions from PostgreSQL database.
/// Called during startup to populate the world version cache.
/// Returns list of active world versions.
/// </summary>
static async Task<List<IWorldVersionCache.WorldVersionInfo>> LoadActiveWorldVersionsFromDatabaseAsync(
    Npgsql.NpgsqlDataSource dataSource,
    ILogger logger)
{
    const string sql = @"
        SELECT id, version, is_active 
        FROM world_versions 
        WHERE is_active = true
        ORDER BY version ASC";

    var versions = new List<IWorldVersionCache.WorldVersionInfo>();

    try
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            versions.Add(new IWorldVersionCache.WorldVersionInfo
            {
                Id = reader.GetInt64(0),
                Version = reader.GetString(1),
                IsActive = reader.GetBoolean(2)
            });
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "❌ Failed to query world_versions table from database");
        throw;
    }

    return versions;
}

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
