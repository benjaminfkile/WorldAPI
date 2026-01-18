using WorldApi.World;
using Amazon.S3;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Services

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configuration
builder.Services.Configure<WorldConfig>(builder.Configuration.GetSection("World"));

// AWS S3
builder.Services.AddSingleton<IAmazonS3>(sp => new AmazonS3Client());

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
    var connectionString = builder.Configuration.GetConnectionString("WorldDb") ?? throw new InvalidOperationException("WorldDb connection string not configured");
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
