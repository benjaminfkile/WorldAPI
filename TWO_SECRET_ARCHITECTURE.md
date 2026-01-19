# Two-Secret Architecture: AWS Secrets Manager Refactor

## Overview

The World API now uses **two separate AWS Secrets Manager secrets** with clear ownership boundaries:

1. **RDS-managed secret** (AWS-owned)
   - Contains database connection credentials
   - Created and rotated by Amazon RDS
   - Read-only from the application perspective
   - Infrastructure credentials only

2. **Application-managed secret** (app-owned)
   - Contains database name and application configuration
   - Created and managed by the World API team
   - Separate from infrastructure concerns
   - Easy to customize per deployment

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                  AWS Secrets Manager                         │
├──────────────────────┬──────────────────────────────────────┤
│                      │                                       │
│  RDS-Managed Secret  │  Application-Managed Secret          │
│  (AWS-owned)         │  (App-owned)                          │
│                      │                                       │
│  ┌────────────────┐  │  ┌──────────────────────────────┐    │
│  │ username       │  │  │ database                      │    │
│  │ password       │  │  │ worldVersion                  │    │
│  │ host           │  │  │                               │    │
│  │ port: 5432     │  │  │ (extensible for future        │    │
│  │ (engine)       │  │  │  application settings)        │    │
│  │ (dbname)       │  │  └──────────────────────────────┘    │
│  └────────────────┘  │                                       │
└──────────────────────┴──────────────────────────────────────┘
           ▲                          ▲
           │                          │
           └──────────────────────────┤
                    Program.cs
                    (Load both at
                     startup)
                         │
                         ▼
           ┌─────────────────────────────────┐
           │  Connection String              │
           │  ─────────────────────────────  │
           │  Host=...;Port=5432;            │
           │  Database=worldapi;             │
           │  Username=...;Password=...      │
           └─────────────────────────────────┘
```

## Environment Variables

```bash
# RDS-managed secret (AWS-owned)
AWS_RDS_SECRET_ARN=arn:aws:secretsmanager:REGION:ACCOUNT:secret:rds!db-ABCDE

# Application-managed secret (app-owned)
AWS_APP_SECRET_ARN=arn:aws:secretsmanager:REGION:ACCOUNT:secret:worldapi-app-config

# AWS Configuration
AWS_REGION=us-east-1
```

## Secret JSON Formats

### RDS-Managed Secret

This secret is created automatically by Amazon RDS. It contains:

```json
{
  "username": "postgres",
  "password": "your-secure-password",
  "engine": "postgres",
  "host": "mydb.abcdef123.us-east-1.rds.amazonaws.com",
  "port": 5432,
  "dbname": "postgres"
}
```

**Note:** The application only uses: `username`, `password`, `host`, `port`

The `dbname` field is **ignored** - the database name comes from the application secret instead.

### Application-Managed Secret

Created and managed by your application team:

```json
{
  "database": ,
  "worldVersion": "world-v1"
}
```

**Extensible:** You can add more application settings here without modifying infrastructure credentials:

```json
{
  "database": ,
  "worldVersion": "world-v1",
  "maxChunkSize": 256,
  "cacheSize": 100,
  "enableDiagnostics": false
}
```

## POCO Models

### RdsDbSecrets (Infrastructure)
```csharp
public class RdsDbSecrets
{
    public string Username { get; set; }        // From RDS secret
    public string Password { get; set; }        // From RDS secret
    public string Host { get; set; }            // From RDS secret
    public int Port { get; set; }               // From RDS secret
}
```

### WorldAppSecrets (Application)
```csharp
public class WorldAppSecrets
{
    public string Database { get; set; }        // From app secret
    public string WorldVersion { get; set; }    // From app secret
}
```

## Startup Flow

1. **Read environment variables**
   - `AWS_RDS_SECRET_ARN`
   - `AWS_APP_SECRET_ARN`
   - `AWS_REGION`

2. **Fetch both secrets concurrently**
   - `GetRdsDbSecretsAsync(rdsSecretArn)` → `RdsDbSecrets`
   - `GetWorldAppSecretsAsync(appSecretArn)` → `WorldAppSecrets`

3. **Build connection string**
   ```
   Host={rds.Host};Port={rds.Port};Database={app.Database};
   Username={rds.Username};Password={rds.Password}
   ```

4. **Register in DI container**
   - `WorldAppSecrets` as singleton (for access via `IOptions<WorldAppSecrets>`)
   - Repository with resolved connection string

5. **Log successful initialization** (without exposing credentials)

## Creating Secrets in AWS

### RDS-Managed Secret

If not using Amazon RDS with automatic secret creation, manually create:

```bash
aws secretsmanager create-secret \
  --name rds!db-instance-id-PROD \
  --secret-string '{
    "username": "postgres",
    "password": "secure-password",
    "engine": "postgres",
    "host": "mydb.rds.amazonaws.com",
    "port": 5432,
    "dbname": "postgres"
  }' \
  --region us-east-1
```

### Application-Managed Secret

Create this secret for your deployment:

```bash
aws secretsmanager create-secret \
  --name worldapi-app-config-prod \
  --description "World API application configuration" \
  --secret-string '{
    "database": ,
    "worldVersion": "world-v1"
  }' \
  --region us-east-1
```

## IAM Permissions

The EC2 instance or Lambda role needs:

```json
{
  "Effect": "Allow",
  "Action": ["secretsmanager:GetSecretValue"],
  "Resource": [
    "arn:aws:secretsmanager:us-east-1:*:secret:rds!db-*",
    "arn:aws:secretsmanager:us-east-1:*:secret:worldapi-app-config-*"
  ]
}
```

## Benefits of Two-Secret Design

✅ **Clear Ownership**
- Infrastructure team owns RDS secret (created by RDS)
- Application team owns app secret (created by developers)

✅ **Independent Rotation**
- RDS credentials can be rotated without code changes
- Application settings can be updated independently
- Different TTLs for each secret

✅ **Reduced Blast Radius**
- Compromised app secret doesn't expose database credentials
- Compromised RDS secret doesn't expose application config

✅ **Flexibility**
- Easy to add new application settings
- No need to coordinate with infrastructure team
- Can customize per environment (dev/staging/prod)

✅ **AWS Best Practice**
- Follows AWS Well-Architected Framework
- Matches how RDS automatic secret management works
- Clear separation of concerns

## Migration Path

If upgrading from single-secret design:

1. Create new RDS-managed secret (or use RDS automatic rotation)
2. Create new application-managed secret with database name
3. Deploy updated application code
4. Update environment variables in deployment
5. Old single secret can be deprecated after verification

## Troubleshooting

| Error | Solution |
|-------|----------|
| `AWS_RDS_SECRET_ARN environment variable is required` | Ensure env var is set |
| `AWS_APP_SECRET_ARN environment variable is required` | Ensure env var is set |
| `RDS secret not found` | Verify ARN and secret exists |
| `Application secret not found` | Verify ARN and secret exists |
| `Failed to deserialize RDS secret JSON` | Verify secret contains required fields |
| `Application secret is missing required field: database` | Verify app secret has `database` field |
| `User is not authorized` | Check IAM role has `secretsmanager:GetSecretValue` |

## Code Examples

### Accessing Application Settings

In any service with dependency injection:

```csharp
public class MyService
{
    private readonly IOptions<WorldAppSecrets> _appSecrets;

    public MyService(IOptions<WorldAppSecrets> appSecrets)
    {
        _appSecrets = appSecrets;
    }

    public void DoSomething()
    {
        var database = _appSecrets.Value.Database;      // 
        var version = _appSecrets.Value.WorldVersion;   // "world-v1"
    }
}
```

### Accessing Secrets in Program.cs (before DI registration)

Both secrets are available as local variables during startup:

```csharp
var rdsSecrets = await secretsManager.GetRdsDbSecretsAsync(rdsSecretArn);
var appSecrets = await secretsManager.GetWorldAppSecretsAsync(appSecretArn);

// Use them to build connection string and register services
```

## Future Extensions

The two-secret design makes it easy to add more application settings without changing the architecture:

```json
{
  "database": ,
  "worldVersion": "world-v1",
  "maxChunkSize": 256,
  "cacheExpirationMinutes": 60,
  "enableTerrainCaching": true,
  "logLevel": "Information"
}
```

Just update the `WorldAppSecrets` POCO to add new properties, and they'll be automatically available throughout the application.
