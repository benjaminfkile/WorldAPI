# AWS Secrets Manager Migration - Implementation Summary

## Architecture

The World API uses **two separate AWS Secrets Manager secrets** with clear ownership:

1. **RDS-managed secret** (AWS-owned) - Database credentials only
2. **Application-managed secret** (app-owned) - Database name + application configuration

See [TWO_SECRET_ARCHITECTURE.md](TWO_SECRET_ARCHITECTURE.md) for detailed architecture documentation.

## What Was Changed

### 1. New Files Created

#### Configuration/RdsDbSecrets.cs
POCO model for RDS-managed secret (infrastructure credentials):
- Properties: `Username`, `Password`, `Host`, `Port`
- Read-only from application perspective
- Created and managed by Amazon RDS

#### Configuration/WorldAppSecrets.cs
POCO model for application-managed secret (application configuration):
- Properties: `Database`, `WorldVersion`
- Extensible for future application settings
- Created and managed by application team

#### Configuration/SecretsManagerService.cs (Updated)
Service that fetches and deserializes both secrets:
- Method: `GetRdsDbSecretsAsync(string secretArn)` - Fetches RDS credentials
- Method: `GetWorldAppSecretsAsync(string secretArn)` - Fetches app configuration
- Both methods include comprehensive error handling and logging
- Validates all required fields are present for each secret type

### 2. Modified Files

#### Program.cs
Key changes:
1. Added loading of both secrets from environment variables:
   - `AWS_RDS_SECRET_ARN` - ARN of RDS-managed secret
   - `AWS_APP_SECRET_ARN` - ARN of application-managed secret
2. Fetches both secrets **concurrently** at startup for better performance
3. Builds connection string using:
   - RDS credentials: `Host`, `Port`, `Username`, `Password`
   - App config: `Database`
4. Registers `WorldAppSecrets` in DI as singleton
5. Logs successful retrieval (without exposing sensitive data)
6. Updated `WorldChunkRepository` registration to use resolved connection string

#### WorldApi.csproj
- Added `AWSSDK.SecretsManager` NuGet package (v4.0.0.3)

#### appsettings.json
- **REMOVED** the entire `ConnectionStrings` section containing database credentials

### 3. Deleted Files

#### Configuration/WorldDbSecrets.cs (Deprecated)
- Old combined POCO class has been removed
- Functionality split into `RdsDbSecrets` and `WorldAppSecrets`

### 4. Documentation Files

See [TWO_SECRET_ARCHITECTURE.md](TWO_SECRET_ARCHITECTURE.md) for the comprehensive architecture guide.

## How It Works

### Startup Flow
1. Application reads environment variables
2. Fetches RDS-managed secret (credentials) concurrently
3. Fetches application-managed secret (config) concurrently
4. Deserializes both into strongly-typed POCOs
5. Builds PostgreSQL connection string combining both secrets
6. Registers `WorldAppSecrets` in DI as singleton for application access
7. Registers repositories with the resolved connection string
8. Logs successful initialization (host, port, database only)

### Ownership Model

| Aspect | RDS Secret | App Secret |
|--------|-----------|-----------|
| Owner | Amazon RDS | Application Team |
| Created By | AWS RDS Service | Developer |
| Content | Credentials | Configuration |
| Rotation | AWS-managed | Manual/External |
| Extensibility | Fixed | Highly Extensible |
| Blast Radius | High (credentials) | Medium (config) |

## Required Environment Variables

```bash
AWS_REGION=us-east-1
AWS_RDS_SECRET_ARN=arn:aws:secretsmanager:us-east-1:123456789:secret:rds!db-prod
AWS_APP_SECRET_ARN=arn:aws:secretsmanager:us-east-1:123456789:secret:worldapi-app-config
```

## Secret JSON Structures

### RDS-Managed Secret (AWS-created)

```json
{
  "username": "postgres",
  "password": "secure-password",
  "engine": "postgres",
  "host": "mydb.rds.amazonaws.com",
  "port": 5432,
  "dbname": "postgres"
}
```

**Note:** Application uses `username`, `password`, `host`, `port` only. `dbname` is ignored.

### Application-Managed Secret (app-created)

```json
{
  "database": ,
  "worldVersion": "world-v1"
}
```

**Extensible:** Additional application settings can be added without infrastructure changes.

## Deployment Checklist

- [ ] Create RDS-managed secret in AWS (or use RDS automatic secret rotation)
- [ ] Create application-managed secret in AWS
- [ ] Copy both secret ARNs
- [ ] Update EC2 IAM role with `secretsmanager:GetSecretValue` permission
- [ ] Update GitHub Secrets with ARN environment variables
- [ ] Update deployment scripts to pass environment variables to Docker container
- [ ] Test locally with environment variables
- [ ] Deploy to dev and verify
- [ ] Deploy to prod and verify

## IAM Permissions Required

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["secretsmanager:GetSecretValue"],
      "Resource": [
        "arn:aws:secretsmanager:REGION:ACCOUNT:secret:rds!db-*",
        "arn:aws:secretsmanager:REGION:ACCOUNT:secret:worldapi-app-config-*"
      ]
    }
  ]
}
```

## Pattern Comparison

This implementation follows AWS best practices:
- ✅ Two-secret design with clear ownership
- ✅ Infrastructure secrets owned by AWS
- ✅ Application settings owned by developers
- ✅ Independent rotation policies
- ✅ Reduced blast radius
- ✅ Highly extensible architecture

The .NET implementation is idiomatic with:
- Strongly-typed POCOs for each secret type
- Dependency injection for AWS clients
- Concurrent secret fetching at startup
- Async/await patterns
- ILogger integration for observability
