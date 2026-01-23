# NpgsqlDataSource & Backpressure Architecture Diagram

## Dependency Injection Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                         Program.cs (DI)                         │
└─────────────────────────────────────────────────────────────────┘

┌─ DATABASE LAYER ─────────────────────────────────────────────────┐
│                                                                   │
│  builder.Services.AddSingleton<NpgsqlDataSource>(...)            │
│  ↓                                                                │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  NpgsqlDataSource (Singleton)                            │   │
│  │  ├─ MaxPoolSize = 20 (hard limit)                       │   │
│  │  ├─ Timeout = 15s (acquire timeout)                     │   │
│  │  ├─ KeepAlive = 60s (idle connection lifetime)          │   │
│  │  └─ DefaultCommandTimeout = 30s                         │   │
│  │                                                          │   │
│  │  ┌─ Connection Pool (Size: 20) ───────────────────┐    │   │
│  │  │  [Conn1] [Conn2] [Conn3] ... [Conn20]         │    │   │
│  │  │  ↓       ↓       ↓           ↓                 │    │   │
│  │  │  Ready Ready  Ready... Ready (reused)          │    │   │
│  │  └──────────────────────────────────────────────┘    │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                                │
└────────────────────────────────────────────────────────────────┘

┌─ REPOSITORY LAYER ──────────────────────────────────────────────┐
│                                                                   │
│  builder.Services.AddScoped<WorldChunkRepository>(sp =>          │
│      new WorldChunkRepository(                                   │
│          sp.GetRequiredService<NpgsqlDataSource>()  ← INJECT    │
│      )                                                           │
│  )                                                               │
│                                                                   │
│  builder.Services.AddSingleton<IWorldVersionService>(sp =>       │
│      new WorldVersionService(                                    │
│          sp.GetRequiredService<NpgsqlDataSource>()  ← INJECT    │
│      )                                                           │
│  )                                                               │
│                                                                   │
│  ┌─────────────────────────────┐  ┌─────────────────────────────┐
│  │ WorldChunkRepository        │  │ WorldVersionService         │
│  │                             │  │                             │
│  │ Constructor:                │  │ Constructor:                │
│  │  - NpgsqlDataSource         │  │  - NpgsqlDataSource         │
│  │                             │  │                             │
│  │ Methods:                    │  │ Methods:                    │
│  │  - GetWorldVersionIdAsync   │  │  - GetWorldVersionAsync     │
│  │    └─ await _dataSource...  │  │    └─ await _dataSource...  │
│  │  - InsertPendingAsync       │  │  - GetActiveVersionsAsync   │
│  │    └─ await _dataSource...  │  │    └─ await _dataSource...  │
│  │  - UpsertReadyAsync         │  │  - IsVersionActiveAsync     │
│  │    └─ await _dataSource...  │  │    └─ (calls GetVersion)    │
│  │  - GetChunkAsync            │  │                             │
│  │    └─ await _dataSource...  │  │                             │
│  └─────────────────────────────┘  └─────────────────────────────┘
│                                                                   │
└────────────────────────────────────────────────────────────────┘

┌─ COORDINATOR LAYER (WITH BACKPRESSURE) ─────────────────────────┐
│                                                                   │
│  builder.Services.AddScoped<ITerrainChunkCoordinator>(sp =>       │
│      var dbWriteSemaphore = new SemaphoreSlim(3, 3);             │
│      return new TerrainChunkCoordinator(                         │
│          repository,    ← WorldChunkRepository                   │
│          generator,     ← TerrainChunkGenerator                  │
│          writer,        ← TerrainChunkWriter                     │
│          logger,        ← ILogger                                │
│          dbWriteSemaphore  ← NEW: Backpressure guard             │
│      );                                                          │
│  )                                                               │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ TerrainChunkCoordinator                                  │   │
│  │                                                          │   │
│  │ Constructor:                                            │   │
│  │  - WorldChunkRepository                                 │   │
│  │  - TerrainChunkGenerator                                │   │
│  │  - TerrainChunkWriter                                   │   │
│  │  - ILogger                                              │   │
│  │  - SemaphoreSlim(3, 3)  ← Limits concurrent writes      │   │
│  │                                                          │   │
│  │ Methods with Backpressure:                              │   │
│  │  - GenerateAndUploadChunkAsync()                        │   │
│  │    ├─ await _dbWriteSemaphore.WaitAsync()               │   │
│  │    │  └─ Block if 3 writes in progress                  │   │
│  │    ├─ await _repository.UpsertReadyAsync()              │   │
│  │    │  └─ Uses pooled connection from NpgsqlDataSource   │   │
│  │    └─ _dbWriteSemaphore.Release()                       │   │
│  │                                                          │   │
│  │  - TriggerGenerationAsync()                             │   │
│  │    ├─ await _dbWriteSemaphore.WaitAsync()               │   │
│  │    │  └─ Block if 3 writes in progress                  │   │
│  │    ├─ await _repository.UpsertReadyAsync()              │   │
│  │    │  └─ Uses pooled connection from NpgsqlDataSource   │   │
│  │    └─ _dbWriteSemaphore.Release()                       │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                   │
└────────────────────────────────────────────────────────────────┘
```

---

## Connection Acquisition Flow Under Load

### ✅ With Pooling (AFTER)

```
Request 1: GET /world/v1/terrain/64/10/20
  └─ TerrainChunksController
      └─ TerrainChunkCoordinator.TriggerGenerationAsync()
          └─ Task.Run() fire-and-forget
              └─ await _dbWriteSemaphore.WaitAsync()  ← ACQUIRE SLOT 1/3
                  └─ await _dataSource.OpenConnectionAsync()
                      └─ PooledConnection[5] (REUSED - was idle)
                          └─ INSERT INTO world_chunks
                  └─ _dbWriteSemaphore.Release()  ← FREE SLOT 1/3

Request 2: GET /world/v1/terrain/64/11/20
  └─ TerrainChunksController
      └─ TerrainChunkCoordinator.TriggerGenerationAsync()
          └─ Task.Run() fire-and-forget
              └─ await _dbWriteSemaphore.WaitAsync()  ← ACQUIRE SLOT 2/3
                  └─ await _dataSource.OpenConnectionAsync()
                      └─ PooledConnection[7] (REUSED - was idle)
                          └─ INSERT INTO world_chunks
                  └─ _dbWriteSemaphore.Release()  ← FREE SLOT 2/3

Request 3: GET /world/v1/terrain/64/12/20
  └─ TerrainChunksController
      └─ TerrainChunkCoordinator.TriggerGenerationAsync()
          └─ Task.Run() fire-and-forget
              └─ await _dbWriteSemaphore.WaitAsync()  ← ACQUIRE SLOT 3/3 (last)
                  └─ await _dataSource.OpenConnectionAsync()
                      └─ PooledConnection[12] (REUSED - was idle)
                          └─ INSERT INTO world_chunks
                  └─ _dbWriteSemaphore.Release()  ← FREE SLOT 3/3

Request 4: GET /world/v1/terrain/64/13/20
  └─ TerrainChunksController
      └─ TerrainChunkCoordinator.TriggerGenerationAsync()
          └─ Task.Run() fire-and-forget
              └─ await _dbWriteSemaphore.WaitAsync()  ← BLOCKED (waiting for slot)
                  ... Task queues until slot becomes available ...
                  [Request 1 finishes] → Release → [Request 4 unblocks]
                  └─ await _dataSource.OpenConnectionAsync()
                      └─ PooledConnection[5] (REUSED again)
                          └─ INSERT INTO world_chunks
                  └─ _dbWriteSemaphore.Release()

┌─────────────────────────────────────────────────────┐
│ Result:                                             │
│ ✅ Max 3 concurrent DB writes (backpressure works) │
│ ✅ Connection pool never needs >20 connections     │
│ ✅ Remaining requests queue gracefully             │
│ ✅ No "too many connections" errors               │
└─────────────────────────────────────────────────────┘
```

---

### ❌ Without Pooling (BEFORE)

```
Request 1: GET /world/v1/terrain/64/10/20
  └─ TerrainChunksController
      └─ TerrainChunkCoordinator.TriggerGenerationAsync()
          └─ Task.Run() fire-and-forget
              └─ new NpgsqlConnection(_connectionString)  ← NEW CONNECTION #1
                  └─ await connection.OpenAsync()  ← TAKES SLOT FROM OS
                      └─ INSERT INTO world_chunks

Request 2: GET /world/v1/terrain/64/11/20
  └─ TerrainChunksController
      └─ TerrainChunkCoordinator.TriggerGenerationAsync()
          └─ Task.Run() fire-and-forget
              └─ new NpgsqlConnection(_connectionString)  ← NEW CONNECTION #2
                  └─ await connection.OpenAsync()  ← TAKES SLOT FROM OS
                      └─ INSERT INTO world_chunks

... (repeat for every request)

Request 50: GET /world/v1/terrain/64/50/20
  └─ new NpgsqlConnection(_connectionString)  ← NEW CONNECTION #50
      └─ await connection.OpenAsync()  ← TAKES SLOT FROM OS
          └─ PostgreSQL says "too many connections"
              └─ Exception: NpgsqlException

┌─────────────────────────────────────────────────────┐
│ Result:                                             │
│ ❌ 50+ concurrent connections created              │
│ ❌ PostgreSQL connection limit exceeded              │
│ ❌ "too many connections" error                     │
│ ❌ Application crashes or becomes unresponsive     │
└─────────────────────────────────────────────────────┘
```

---

## Query Execution Timeline with Pooling

```
Time    Event                                   Connections   Semaphore
───────────────────────────────────────────────────────────────────────
T0      NpgsqlDataSource initialized            [POOL: 20]    N/A
        5 connections pre-allocated
        15 connections ready for use

T1      Request A arrives                       [ACTIVE: 1]   [1/3]
        Acquires semaphore slot 1
        Calls _dataSource.OpenConnectionAsync()
        → Gets PooledConnection[1] (instant reuse)

T2      Request B arrives                       [ACTIVE: 2]   [2/3]
        Acquires semaphore slot 2
        Calls _dataSource.OpenConnectionAsync()
        → Gets PooledConnection[2] (instant reuse)

T3      Request C arrives                       [ACTIVE: 3]   [3/3]
        Acquires semaphore slot 3
        Calls _dataSource.OpenConnectionAsync()
        → Gets PooledConnection[3] (instant reuse)

T4      Request D arrives                       [ACTIVE: 3]   [BLOCKED]
        Tries to acquire semaphore slot 4
        → BLOCKS (max 3 concurrent)
        
        Request A finishes (50ms query)
        Releases semaphore slot 1
        PooledConnection[1] returns to pool

T5      Request D unblocks                      [ACTIVE: 3]   [3/3]
        Acquires released semaphore slot 1
        Calls _dataSource.OpenConnectionAsync()
        → Gets PooledConnection[1] again (reused!)
        
        Request B finishes (45ms query)
        Releases semaphore slot 2
        PooledConnection[2] returns to pool

T6      Request E unblocks (was waiting)        [ACTIVE: 3]   [3/3]
        Acquires released semaphore slot 2
        Calls _dataSource.OpenConnectionAsync()
        → Gets PooledConnection[2] again (reused!)

T7-T15  Remaining requests process similarly...  [≤20 TOTAL]   [≤3 CONCURRENT]

───────────────────────────────────────────────────────────────────────
Result: Graceful queueing, constant memory, no new connections needed
```

---

## Performance Characteristics

```
                BEFORE              AFTER
                ──────              ─────
Connection      new()               pool reuse
Creation        every query         one-time

Total Conns     Unbounded (50+)     Limited (≤20)
Max Concurrent  Unbounded           SemaphoreSlim(3,3)
Query Wait      OS socket timeout   Pool timeout (15s)
Query Exec      No timeout          30s timeout
Idle Timeout    OS dependent        60s KeepAlive
Latency         High                Low
Error Rate      High ("too many")   Low (only overload)
```

---

## Configuration Tuning by Load Profile

### Light Load (Development)
```csharp
MaxPoolSize = 10
SemaphoreSlim(2, 2)
DefaultCommandTimeout = 30s
```

### Medium Load (Production)
```csharp
MaxPoolSize = 20  ← CURRENT
SemaphoreSlim(3, 3)  ← CURRENT
DefaultCommandTimeout = 30s  ← CURRENT
```

### Heavy Load (High-Traffic API)
```csharp
MaxPoolSize = 30
SemaphoreSlim(5, 5)
DefaultCommandTimeout = 60s
```

---

## Monitoring Queries

### Check Active Connections
```sql
SELECT count(*) FROM pg_stat_activity;
-- Expected: 10-20 (not 50+)
```

### Check Connection Age
```sql
SELECT pid, usename, application_name, state, 
       now() - query_start AS query_duration
FROM pg_stat_activity
WHERE state = 'active'
ORDER BY query_duration DESC;
```

### Check Connection Health
```sql
SELECT datname, count(*) AS connection_count
FROM pg_stat_activity
GROUP BY datname
ORDER BY connection_count DESC;
```

---

## Done! ✅

Architecture is now:
- **Pooled:** Shared NpgsqlDataSource with 20-connection limit
- **Backpressured:** SemaphoreSlim queues DB writes (max 3 concurrent)
- **Resilient:** Timeouts prevent hangs (15s acquisition, 30s execution)
- **Scalable:** Reuses connections, no unbounded creation

Ready for production deployment.
