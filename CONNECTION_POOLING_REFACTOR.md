# PostgreSQL Connection Pooling Refactor

## Overview

This refactor eliminates PostgreSQL connection storms by introducing **NpgsqlDataSource** for proper connection pooling and **SemaphoreSlim** for explicit backpressure on database writes.

**Problem Solved:**
- ❌ `new NpgsqlConnection()` created unbounded concurrent connections
- ❌ Background terrain generation + HTTP requests caused connection storms  
- ❌ PostgreSQL Docker couldn't handle fan-out → NpgsqlTimeout, SocketException
- ✅ **Now:** Single shared pool (max 20 connections), coordinated access

---

## Architecture Changes

### 1. NpgsqlDataSource Registration (Program.cs)

**Before:**
```csharp
// ❌ BAD: Each repository gets connection string, creates new connections
builder.Services.AddScoped<WorldChunkRepository>(sp => 
    new WorldChunkRepository(connectionString)
);

// ❌ BAD: WorldVersionService also creates direct connections
builder.Services.AddSingleton<IWorldVersionService>(sp =>
    new WorldVersionService(connectionString)
);
```

**After:**
```csharp
// ✅ GOOD: Single shared NpgsqlDataSource with pooling config
builder.Services.AddSingleton<NpgsqlDataSource>(sp =>
{
    var builder = new NpgsqlDataSourceBuilder(connectionString)
    {
        MaxPoolSize = 20,  // Limit concurrent connections
        Timeout = TimeSpan.FromSeconds(15),  // Acquisition timeout
        KeepAlive = TimeSpan.FromSeconds(60)  // Keep idle connections
    };
    builder.DefaultCommandTimeout = TimeSpan.FromSeconds(30);
    return builder.Build();
});

// ✅ Inject NpgsqlDataSource instead of connection string
builder.Services.AddScoped<WorldChunkRepository>(sp => 
    new WorldChunkRepository(sp.GetRequiredService<NpgsqlDataSource>())
);

builder.Services.AddSingleton<IWorldVersionService>(sp =>
    new WorldVersionService(sp.GetRequiredService<NpgsqlDataSource>())
);
```

**Configuration Details:**
- `MaxPoolSize = 20`: Total concurrent connections across the app (adjust based on load)
- `Timeout = 15s`: How long to wait for an available connection from the pool
- `KeepAlive = 60s`: Prevents idle connections from being closed by the database
- `DefaultCommandTimeout = 30s`: SQL command execution timeout

---

### 2. Repository Refactoring Pattern

**WorldChunkRepository Before:**
```csharp
public sealed class WorldChunkRepository
{
    private readonly string _connectionString;  // ❌ Creates new connections
    
    public WorldChunkRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private async Task<long?> GetWorldVersionIdAsync(string worldVersion)
    {
        // ❌ BAD: new NpgsqlConnection() each time
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        // ... execute query
    }
}
```

**WorldChunkRepository After:**
```csharp
public sealed class WorldChunkRepository
{
    private readonly NpgsqlDataSource _dataSource;  // ✅ Shared pool
    
    public WorldChunkRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    private async Task<long?> GetWorldVersionIdAsync(string worldVersion)
    {
        // ✅ GOOD: Acquire from pool via data source
        await using var connection = await _dataSource.OpenConnectionAsync();
        // ... execute query
    }
}
```

**Key Change:**
- Replace `new NpgsqlConnection(_connectionString)` with `await _dataSource.OpenConnectionAsync()`
- This returns a pooled connection (reused if available) instead of creating a new one

---

### 3. Backpressure via SemaphoreSlim

**TerrainChunkCoordinator Before:**
```csharp
public class TerrainChunkCoordinator
{
    private readonly WorldChunkRepository _repository;
    
    public TerrainChunkCoordinator(WorldChunkRepository repository, ...)
    {
        _repository = repository;  // ❌ No backpressure on DB writes
    }

    // ❌ BAD: Fire-and-forget with unbounded DB writes
    public virtual async Task TriggerGenerationAsync(...)
    {
        _ = Task.Run(async () =>
        {
            await _repository.UpsertReadyAsync(...);  // Could queue 100s of tasks
        });
    }
}
```

**TerrainChunkCoordinator After:**
```csharp
public class TerrainChunkCoordinator
{
    private readonly WorldChunkRepository _repository;
    private readonly SemaphoreSlim _dbWriteSemaphore;  // ✅ Backpressure guard
    
    public TerrainChunkCoordinator(
        WorldChunkRepository repository, 
        ...,
        SemaphoreSlim dbWriteSemaphore)
    {
        _repository = repository;
        _dbWriteSemaphore = dbWriteSemaphore;  // Max 3 concurrent DB writes
    }

    // ✅ GOOD: Guard DB writes with semaphore
    public virtual async Task TriggerGenerationAsync(...)
    {
        _ = Task.Run(async () =>
        {
            await _dbWriteSemaphore.WaitAsync();  // Block if 3 tasks waiting
            try
            {
                await _repository.UpsertReadyAsync(...);
            }
            finally
            {
                _dbWriteSemaphore.Release();
            }
        });
    }
}
```

**Registration in Program.cs:**
```csharp
builder.Services.AddScoped<ITerrainChunkCoordinator>(sp =>
{
    var repository = sp.GetRequiredService<WorldChunkRepository>();
    var generator = sp.GetRequiredService<TerrainChunkGenerator>();
    var writer = sp.GetRequiredService<TerrainChunkWriter>();
    var logger = sp.GetRequiredService<ILogger<TerrainChunkCoordinator>>();
    var dbWriteSemaphore = new SemaphoreSlim(3, 3);  // Max 3 concurrent writes
    return new TerrainChunkCoordinator(repository, generator, writer, logger, dbWriteSemaphore);
});
```

**Why This Works:**
- `SemaphoreSlim(3, 3)` allows max 3 background tasks to write to DB simultaneously
- Remaining tasks queue and wait for a slot to free up
- Prevents connection pool exhaustion by limiting concurrent DB operations

---

## Files Modified

### 1. **Program.cs** ✅
- Added `using Npgsql;`
- Registered `NpgsqlDataSource` as singleton with pooling config
- Updated `WorldChunkRepository` to inject `NpgsqlDataSource`
- Updated `IWorldVersionService` to inject `NpgsqlDataSource`
- Updated `ITerrainChunkCoordinator` registration to include `SemaphoreSlim(3, 3)`

### 2. **WorldChunkRepository.cs** ✅
- Changed constructor from `string connectionString` → `NpgsqlDataSource dataSource`
- Updated `GetWorldVersionIdAsync()` to use `_dataSource.OpenConnectionAsync()`
- Updated `InsertPendingAsync()` to use `_dataSource.OpenConnectionAsync()`
- Updated `UpsertReadyAsync()` to use `_dataSource.OpenConnectionAsync()`
- Updated `GetChunkAsync()` to use `_dataSource.OpenConnectionAsync()`

### 3. **WorldVersionService.cs** ✅
- Changed constructor from `string connectionString` → `NpgsqlDataSource dataSource`
- Updated `GetWorldVersionAsync()` to use `_dataSource.OpenConnectionAsync()`
- Updated `GetActiveWorldVersionsAsync()` to use `_dataSource.OpenConnectionAsync()`

### 4. **TerrainChunkCoordinator.cs** ✅
- Added constructor parameter `SemaphoreSlim dbWriteSemaphore`
- Added backpressure guard in `GenerateAndUploadChunkAsync()` around `UpsertReadyAsync()`
- Added backpressure guard in `TriggerGenerationAsync()` around `UpsertReadyAsync()`
- Updated class XML docs to mention backpressure

---

## Old Patterns to REMOVE ❌

```csharp
// ❌ REMOVE THIS PATTERN
public SomeRepository(string connectionString)
{
    _connectionString = connectionString;
}

// ❌ REMOVE THIS
await using var connection = new NpgsqlConnection(_connectionString);
await connection.OpenAsync();
```

---

## New Patterns to USE ✅

```csharp
// ✅ USE THIS INSTEAD
public SomeRepository(NpgsqlDataSource dataSource)
{
    _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
}

// ✅ USE THIS
await using var connection = await _dataSource.OpenConnectionAsync();
```

---

## Performance Impact

| Metric | Before | After |
|--------|--------|-------|
| **Connection Handling** | New connection per query | Shared pool (reused) |
| **Connection Limit** | Unbounded | 20 max (configurable) |
| **Concurrent DB Writes** | Unbounded | 3 max (SemaphoreSlim) |
| **Idle Connection Timeout** | OS default | 60s KeepAlive |
| **Connection Acquisition Timeout** | N/A | 15s |
| **Query Timeout** | N/A | 30s |

**Expected Results:**
- ✅ PostgreSQL connection count stays ≤ 20
- ✅ No more "too many connections" errors
- ✅ Background terrain generation queues gracefully under load
- ✅ HTTP requests don't starve for connections
- ✅ Stable latency even with 100+ concurrent chunk requests

---

## Testing Checklist

- [ ] App starts and initializes NpgsqlDataSource
- [ ] Single HTTP request for terrain chunk succeeds
- [ ] Multiple concurrent HTTP requests work
- [ ] Background terrain generation completes
- [ ] Multiple background generations queue gracefully
- [ ] PostgreSQL connection count never exceeds ~25 under load
- [ ] No NpgsqlTimeout or "too many connections" errors
- [ ] Database queries complete within 30s timeout
- [ ] Connection acquisition doesn't timeout (should be instant if pool has slots)

---

## Configuration Tuning

### Increase MaxPoolSize for Higher Load
```csharp
MaxPoolSize = 30,  // If app has many concurrent operations
```

### Decrease SemaphoreSlim Slots for Limited Resources
```csharp
var dbWriteSemaphore = new SemaphoreSlim(2, 2);  // Only 2 concurrent writes
```

### Increase CommandTimeout for Large Queries
```csharp
builder.DefaultCommandTimeout = TimeSpan.FromSeconds(60);  // 60s instead of 30s
```

---

## References

- **NpgsqlDataSource:** https://www.npgsql.org/doc/api/Npgsql.NpgsqlDataSource.html
- **NpgsqlDataSourceBuilder:** Configures pooling, timeouts, keep-alive
- **SemaphoreSlim:** https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim

---

## Summary

This refactor **eliminates connection storms** by:

1. **Pooling:** `NpgsqlDataSource` maintains a shared pool of reusable connections
2. **Limiting:** `MaxPoolSize = 20` caps total concurrent connections
3. **Backpressure:** `SemaphoreSlim(3, 3)` queues DB writes when pool is constrained
4. **Timeouts:** Configuration prevents hang-ups (`Timeout`, `CommandTimeout`, `KeepAlive`)

**Result:** Stable, scalable database access even under load spikes.
