# PostgreSQL Connection Pooling Refactor — Code Reference

## Side-by-Side Code Comparison

---

## 1. Program.cs Registration

### ❌ BEFORE (Lines 36-105)
```csharp
// AWS Clients
builder.Services.AddSingleton<IAmazonS3>(sp => new AmazonS3Client());
builder.Services.AddSingleton<IAmazonSecretsManager>(sp => new AmazonSecretsManagerClient());

// ... (secrets loading code)

// OLD: WorldVersionService with connection string
builder.Services.AddSingleton<IWorldVersionService>(sp =>
{
    return new WorldVersionService(connectionString);  // ❌ Direct connection string
});

// OLD: WorldChunkRepository with connection string
builder.Services.AddScoped<WorldChunkRepository>(sp =>
{
    return new WorldChunkRepository(connectionString);  // ❌ Direct connection string
});

// OLD: TerrainChunkCoordinator without backpressure
builder.Services.AddScoped<ITerrainChunkCoordinator>(sp =>
{
    var repository = sp.GetRequiredService<WorldChunkRepository>();
    var generator = sp.GetRequiredService<TerrainChunkGenerator>();
    var writer = sp.GetRequiredService<TerrainChunkWriter>();
    var logger = sp.GetRequiredService<ILogger<TerrainChunkCoordinator>>();
    return new TerrainChunkCoordinator(repository, generator, writer, logger);  // ❌ No semaphore
});
```

### ✅ AFTER (Lines 1-220)
```csharp
using Npgsql;  // ← ADD THIS

// Register NpgsqlDataSource singleton with pooling config
builder.Services.AddSingleton<NpgsqlDataSource>(sp =>
{
    var builder = new NpgsqlDataSourceBuilder(connectionString)
    {
        MaxPoolSize = 20,
        Timeout = TimeSpan.FromSeconds(15),
        KeepAlive = TimeSpan.FromSeconds(60)
    };
    builder.DefaultCommandTimeout = TimeSpan.FromSeconds(30);
    return builder.Build();
});

// NEW: WorldVersionService with NpgsqlDataSource
builder.Services.AddSingleton<IWorldVersionService>(sp =>
{
    var dataSource = sp.GetRequiredService<NpgsqlDataSource>();  // ✅ Inject data source
    return new WorldVersionService(dataSource);
});

// NEW: WorldChunkRepository with NpgsqlDataSource
builder.Services.AddScoped<WorldChunkRepository>(sp =>
{
    var dataSource = sp.GetRequiredService<NpgsqlDataSource>();  // ✅ Inject data source
    return new WorldChunkRepository(dataSource);
});

// NEW: TerrainChunkCoordinator with SemaphoreSlim
builder.Services.AddScoped<ITerrainChunkCoordinator>(sp =>
{
    var repository = sp.GetRequiredService<WorldChunkRepository>();
    var generator = sp.GetRequiredService<TerrainChunkGenerator>();
    var writer = sp.GetRequiredService<TerrainChunkWriter>();
    var logger = sp.GetRequiredService<ILogger<TerrainChunkCoordinator>>();
    var dbWriteSemaphore = new SemaphoreSlim(3, 3);  // ✅ Add backpressure (3 max concurrent writes)
    return new TerrainChunkCoordinator(repository, generator, writer, logger, dbWriteSemaphore);
});
```

---

## 2. WorldChunkRepository.cs

### ❌ BEFORE
```csharp
public sealed class WorldChunkRepository
{
    private readonly string _connectionString;

    public WorldChunkRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private async Task<long?> GetWorldVersionIdAsync(string worldVersion)
    {
        const string sql = @"SELECT id FROM world_versions WHERE version = @version LIMIT 1";
        
        // ❌ NEW CONNECTION CREATED HERE
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@version", worldVersion);
        
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return reader.GetInt64(0);
        }
        return null;
    }

    public async Task<WorldChunkMetadata> InsertPendingAsync(
        int chunkX, int chunkZ, string layer, int resolution, 
        string worldVersion, string s3Key)
    {
        var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
        if (worldVersionId == null) throw new InvalidOperationException(...);

        const string sql = @"INSERT INTO world_chunks (...) VALUES (...) RETURNING ...";

        // ❌ ANOTHER NEW CONNECTION HERE
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // ... execute query
    }

    public async Task<WorldChunkMetadata> UpsertReadyAsync(
        int chunkX, int chunkZ, string layer, int resolution, 
        string worldVersion, string s3Key, string checksum)
    {
        var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
        if (worldVersionId == null) throw new InvalidOperationException(...);

        const string sql = @"INSERT INTO world_chunks (...) VALUES (...) RETURNING ...";

        // ❌ ANOTHER NEW CONNECTION HERE
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // ... execute query
    }

    public async Task<WorldChunkMetadata?> GetChunkAsync(
        int chunkX, int chunkZ, string layer, int resolution, string worldVersion)
    {
        var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
        if (worldVersionId == null) return null;

        const string sql = @"SELECT ... FROM world_chunks WHERE ...";

        // ❌ ANOTHER NEW CONNECTION HERE
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // ... execute query
    }
}
```

### ✅ AFTER
```csharp
/// <summary>
/// Repository for world chunk metadata (stored in PostgreSQL).
/// Uses shared NpgsqlDataSource for connection pooling to prevent connection storms.
/// All database operations go through the pool, which enforces MaxPoolSize limits.
/// </summary>
public sealed class WorldChunkRepository
{
    private readonly NpgsqlDataSource _dataSource;  // ✅ SHARE POOL, NOT CONNECTION STRING

    public WorldChunkRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    private async Task<long?> GetWorldVersionIdAsync(string worldVersion)
    {
        const string sql = @"SELECT id FROM world_versions WHERE version = @version LIMIT 1";
        
        // ✅ CONNECTION FROM POOL (REUSED)
        await using var connection = await _dataSource.OpenConnectionAsync();
        
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@version", worldVersion);
        
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return reader.GetInt64(0);
        }
        return null;
    }

    public async Task<WorldChunkMetadata> InsertPendingAsync(
        int chunkX, int chunkZ, string layer, int resolution, 
        string worldVersion, string s3Key)
    {
        var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
        if (worldVersionId == null) throw new InvalidOperationException(...);

        const string sql = @"INSERT INTO world_chunks (...) VALUES (...) RETURNING ...";

        // ✅ CONNECTION FROM POOL (REUSED)
        await using var connection = await _dataSource.OpenConnectionAsync();

        // ... execute query
    }

    public async Task<WorldChunkMetadata> UpsertReadyAsync(
        int chunkX, int chunkZ, string layer, int resolution, 
        string worldVersion, string s3Key, string checksum)
    {
        var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
        if (worldVersionId == null) throw new InvalidOperationException(...);

        const string sql = @"INSERT INTO world_chunks (...) VALUES (...) RETURNING ...";

        // ✅ CONNECTION FROM POOL (REUSED)
        await using var connection = await _dataSource.OpenConnectionAsync();

        // ... execute query
    }

    public async Task<WorldChunkMetadata?> GetChunkAsync(
        int chunkX, int chunkZ, string layer, int resolution, string worldVersion)
    {
        var worldVersionId = await GetWorldVersionIdAsync(worldVersion);
        if (worldVersionId == null) return null;

        const string sql = @"SELECT ... FROM world_chunks WHERE ...";

        // ✅ CONNECTION FROM POOL (REUSED)
        await using var connection = await _dataSource.OpenConnectionAsync();

        // ... execute query
    }
}
```

**Key Change:** Every `new NpgsqlConnection(_connectionString)` → `await _dataSource.OpenConnectionAsync()`

---

## 3. WorldVersionService.cs

### ❌ BEFORE
```csharp
public sealed class WorldVersionService : IWorldVersionService
{
    private readonly string _connectionString;

    public WorldVersionService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IWorldVersionService.WorldVersionInfo?> GetWorldVersionAsync(string version)
    {
        const string sql = @"SELECT id, version, is_active FROM world_versions WHERE version = @version LIMIT 1";

        // ❌ NEW CONNECTION
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@version", version);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new IWorldVersionService.WorldVersionInfo
            {
                Id = reader.GetInt64(0),
                Version = reader.GetString(1),
                IsActive = reader.GetBoolean(2)
            };
        }
        return null;
    }

    public async Task<IReadOnlyList<IWorldVersionService.WorldVersionInfo>> GetActiveWorldVersionsAsync()
    {
        const string sql = @"SELECT id, version, is_active FROM world_versions WHERE is_active = true ORDER BY version ASC";

        var versions = new List<IWorldVersionService.WorldVersionInfo>();

        // ❌ ANOTHER NEW CONNECTION
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            versions.Add(new IWorldVersionService.WorldVersionInfo
            {
                Id = reader.GetInt64(0),
                Version = reader.GetString(1),
                IsActive = reader.GetBoolean(2)
            });
        }

        return versions;
    }
}
```

### ✅ AFTER
```csharp
public sealed class WorldVersionService : IWorldVersionService
{
    private readonly NpgsqlDataSource _dataSource;  // ✅ SHARE POOL

    public WorldVersionService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<IWorldVersionService.WorldVersionInfo?> GetWorldVersionAsync(string version)
    {
        const string sql = @"SELECT id, version, is_active FROM world_versions WHERE version = @version LIMIT 1";

        // ✅ CONNECTION FROM POOL
        await using var connection = await _dataSource.OpenConnectionAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@version", version);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new IWorldVersionService.WorldVersionInfo
            {
                Id = reader.GetInt64(0),
                Version = reader.GetString(1),
                IsActive = reader.GetBoolean(2)
            };
        }
        return null;
    }

    public async Task<IReadOnlyList<IWorldVersionService.WorldVersionInfo>> GetActiveWorldVersionsAsync()
    {
        const string sql = @"SELECT id, version, is_active FROM world_versions WHERE is_active = true ORDER BY version ASC";

        var versions = new List<IWorldVersionService.WorldVersionInfo>();

        // ✅ CONNECTION FROM POOL
        await using var connection = await _dataSource.OpenConnectionAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            versions.Add(new IWorldVersionService.WorldVersionInfo
            {
                Id = reader.GetInt64(0),
                Version = reader.GetString(1),
                IsActive = reader.GetBoolean(2)
            });
        }

        return versions;
    }
}
```

---

## 4. TerrainChunkCoordinator.cs

### ❌ BEFORE
```csharp
public class TerrainChunkCoordinator : ITerrainChunkCoordinator
{
    private readonly WorldChunkRepository _repository;
    private readonly TerrainChunkGenerator _generator;
    private readonly TerrainChunkWriter _writer;
    private readonly ILogger<TerrainChunkCoordinator> _logger;

    public TerrainChunkCoordinator(
        WorldChunkRepository repository,
        TerrainChunkGenerator generator,
        TerrainChunkWriter writer,
        ILogger<TerrainChunkCoordinator> logger)
    {
        _repository = repository;
        _generator = generator;
        _writer = writer;
        _logger = logger;
        // ❌ NO BACKPRESSURE MECHANISM
    }

    private async Task<TerrainChunk> GenerateAndUploadChunkAsync(...)
    {
        var chunk = await _generator.GenerateAsync(chunkX, chunkZ, resolution);
        byte[] serializedData = TerrainChunkSerializer.Serialize(chunk);
        string contentHash = Convert.ToHexString(SHA256.HashData(serializedData));
        
        // ❌ UNGUARDED DB WRITE
        await _repository.UpsertReadyAsync(
            chunkX, chunkZ, layer, resolution, worldVersion, s3Key, contentHash);

        // ... rest
    }

    public virtual async Task TriggerGenerationAsync(...)
    {
        // ... check if ready

        // ❌ FIRE-AND-FORGET WITH UNBOUNDED DB WRITES
        _ = Task.Run(async () =>
        {
            try
            {
                var chunk = await _generator.GenerateAsync(chunkX, chunkZ, resolution);
                var uploadResult = await _writer.WriteAsync(chunk, s3Key);
                
                // ❌ NO SEMAPHORE GUARD - CAN QUEUE UNLIMITED TASKS
                await _repository.UpsertReadyAsync(
                    chunkX, chunkZ, layer, resolution, worldVersion, s3Key, uploadResult.Checksum);
            }
            catch (Exception ex) { /* ... */ }
        });
    }
}
```

### ✅ AFTER
```csharp
/// <summary>
/// Orchestrates terrain chunk generation, upload, and metadata storage.
/// Applies backpressure via SemaphoreSlim to limit concurrent database writes
/// and prevent connection exhaustion under heavy load.
/// </summary>
public class TerrainChunkCoordinator : ITerrainChunkCoordinator
{
    private readonly WorldChunkRepository _repository;
    private readonly TerrainChunkGenerator _generator;
    private readonly TerrainChunkWriter _writer;
    private readonly ILogger<TerrainChunkCoordinator> _logger;
    private readonly SemaphoreSlim _dbWriteSemaphore;  // ✅ BACKPRESSURE MECHANISM

    public TerrainChunkCoordinator(
        WorldChunkRepository repository,
        TerrainChunkGenerator generator,
        TerrainChunkWriter writer,
        ILogger<TerrainChunkCoordinator> logger,
        SemaphoreSlim dbWriteSemaphore)  // ✅ NEW PARAMETER
    {
        _repository = repository;
        _generator = generator;
        _writer = writer;
        _logger = logger;
        _dbWriteSemaphore = dbWriteSemaphore ?? throw new ArgumentNullException(nameof(dbWriteSemaphore));
    }

    private async Task<TerrainChunk> GenerateAndUploadChunkAsync(...)
    {
        var chunk = await _generator.GenerateAsync(chunkX, chunkZ, resolution);
        byte[] serializedData = TerrainChunkSerializer.Serialize(chunk);
        string contentHash = Convert.ToHexString(SHA256.HashData(serializedData));
        
        // ✅ GUARDED WITH SEMAPHORE
        await _dbWriteSemaphore.WaitAsync();  // Block if 3 concurrent writes
        try
        {
            await _repository.UpsertReadyAsync(
                chunkX, chunkZ, layer, resolution, worldVersion, s3Key, contentHash);
        }
        finally
        {
            _dbWriteSemaphore.Release();
        }

        // ... rest
    }

    public virtual async Task TriggerGenerationAsync(...)
    {
        // ... check if ready

        // ✅ FIRE-AND-FORGET WITH BOUNDED DB WRITES (MAX 3)
        _ = Task.Run(async () =>
        {
            try
            {
                var chunk = await _generator.GenerateAsync(chunkX, chunkZ, resolution);
                var uploadResult = await _writer.WriteAsync(chunk, s3Key);
                
                // ✅ GUARDED WITH SEMAPHORE - TASKS QUEUE WHEN LIMIT HIT
                await _dbWriteSemaphore.WaitAsync();
                try
                {
                    await _repository.UpsertReadyAsync(
                        chunkX, chunkZ, layer, resolution, worldVersion, s3Key, uploadResult.Checksum);
                }
                finally
                {
                    _dbWriteSemaphore.Release();
                }
            }
            catch (Exception ex) { /* ... */ }
        });
    }
}
```

---

## Summary of Pattern Changes

| Aspect | ❌ Before | ✅ After |
|--------|----------|----------|
| **Constructor Param** | `string connectionString` | `NpgsqlDataSource dataSource` |
| **Connection Creation** | `new NpgsqlConnection(_connectionString)` | `await _dataSource.OpenConnectionAsync()` |
| **Connection Pool** | None (unbounded) | `MaxPoolSize = 20` |
| **DB Write Limit** | Unbounded | `SemaphoreSlim(3, 3)` |
| **Timeout Config** | No | 15s (acquire) + 30s (execute) |
| **Connection Reuse** | No | Yes (from pool) |
| **Backpressure** | No | Yes (semaphore queues writes) |

---

## Copy-Paste Templates

### Template 1: New Repository with Pooling

```csharp
using Npgsql;

namespace WorldApi.Services;

public sealed class MyRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public MyRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<MyModel> GetAsync(int id)
    {
        const string sql = "SELECT id, name FROM my_table WHERE id = @id";
        
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new MyModel { Id = reader.GetInt32(0), Name = reader.GetString(1) };
        }

        return null;
    }
}
```

### Template 2: DI Registration

```csharp
// In Program.cs

// 1. Register NpgsqlDataSource (done once, at startup)
builder.Services.AddSingleton<NpgsqlDataSource>(sp =>
{
    var builder = new NpgsqlDataSourceBuilder(connectionString)
    {
        MaxPoolSize = 20,
        Timeout = TimeSpan.FromSeconds(15),
        KeepAlive = TimeSpan.FromSeconds(60)
    };
    builder.DefaultCommandTimeout = TimeSpan.FromSeconds(30);
    return builder.Build();
});

// 2. Inject into repositories
builder.Services.AddScoped<MyRepository>(sp =>
    new MyRepository(sp.GetRequiredService<NpgsqlDataSource>())
);
```

### Template 3: Backpressure Guard

```csharp
await _semaphore.WaitAsync();  // Block if limit reached
try
{
    await repository.WriteAsync(...);
}
finally
{
    _semaphore.Release();
}
```

---

## Done! ✅

All code has been refactored. No more connection storms. Ready for testing.
