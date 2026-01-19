# Quick Start Guide - AWS Secrets Manager Integration

## What Changed?
Database credentials are no longer stored in `appsettings.json`. They're now loaded from AWS Secrets Manager at startup.

## Local Development Setup

### 1. Create a Secret in AWS (if not exists)
```bash
aws secretsmanager create-secret \
  --name worldapi-db-dev \
  --description "World API database credentials for development" \
  --secret-string '{
    "host": "localhost",
    "database": ,
    "username": "postgres",
    "password": "postgres",
    "port": 5432
  }' \
  --region us-east-1
```

### 2. Get the Secret ARN
```bash
aws secretsmanager describe-secret \
  --secret-id worldapi-db-dev \
  --region us-east-1 \
  --query 'ARN' \
  --output text
```

### 3. Set Environment Variables
Copy `.env.example` to `.env` (or set in your shell):

```bash
export AWS_REGION=us-east-1
export AWS_DB_SECRET_ARN=arn:aws:secretsmanager:us-east-1:123456789012:secret:worldapi-db-dev-abc123
```

### 4. Ensure AWS Credentials Are Configured
```bash
aws configure
# OR
export AWS_ACCESS_KEY_ID=your-key
export AWS_SECRET_ACCESS_KEY=your-secret
```

### 5. Run the Application
```bash
cd src/WorldApi
dotnet run
```

Expected output:
```
Successfully loaded database configuration from AWS Secrets Manager
Database host: localhost, Database: worldapi
```

## Production Deployment

### 1. Create Production Secret
```bash
aws secretsmanager create-secret \
  --name worldapi-db-prod \
  --secret-string '{
    "host": "your-prod-db.rds.amazonaws.com",
    "database": ,
    "username": "produser",
    "password": "secure-prod-password",
    "port": 5432
  }' \
  --region us-east-1
```

### 2. Update EC2 IAM Role
Add this policy to your EC2 instance role:

```json
{
  "Effect": "Allow",
  "Action": ["secretsmanager:GetSecretValue"],
  "Resource": "arn:aws:secretsmanager:*:*:secret:worldapi-db-*"
}
```

### 3. Update Launch Template User Data
Add environment variables to your Docker run command:

```bash
docker run -d \
  --name worldapi \
  -p 3004:3004 \
  --restart unless-stopped \
  -e AWS_REGION=us-east-1 \
  -e AWS_DB_SECRET_ARN=arn:aws:secretsmanager:us-east-1:ACCOUNT:secret:worldapi-db-prod \
  your-ecr-registry/worldapi:latest
```

### 4. Deploy
Your existing GitHub Actions workflow will build and push the Docker image. The Auto Scaling Group refresh will pick up the new image.

## Testing

### Test Secret Access
```bash
aws secretsmanager get-secret-value \
  --secret-id worldapi-db-dev \
  --region us-east-1
```

### Test Application Startup
```bash
docker logs worldapi
```

Look for:
```
Successfully loaded database configuration from AWS Secrets Manager
Database host: your-db, Database: worldapi
```

## Troubleshooting

| Error | Solution |
|-------|----------|
| `AWS_DB_SECRET_ARN environment variable is required` | Set the `AWS_DB_SECRET_ARN` environment variable |
| `Secret not found` | Verify the ARN is correct and the secret exists |
| `User is not authorized` | Check IAM permissions for `secretsmanager:GetSecretValue` |
| `Failed to deserialize secret` | Verify the secret JSON matches the expected format |

## Files Created/Modified

### Created
- `src/WorldApi/Configuration/WorldDbSecrets.cs` - Secret POCO model
- `src/WorldApi/Configuration/SecretsManagerService.cs` - Service to fetch secrets
- `SECRETS_MANAGER.md` - Detailed documentation
- `DEPLOYMENT_GUIDE.md` - Deployment configuration guide
- `WORKFLOW_UPDATE_GUIDE.md` - GitHub Actions workflow guidance
- `MIGRATION_SUMMARY.md` - Complete implementation summary
- `.env.example` - Environment variable template

### Modified
- `src/WorldApi/Program.cs` - Added secrets loading at startup
- `src/WorldApi/WorldApi.csproj` - Added AWSSDK.SecretsManager package
- `src/WorldApi/appsettings.json` - Removed ConnectionStrings section

## Next Steps

1. ✅ Code changes complete
2. ⏭️ Create secrets in AWS Secrets Manager (dev and prod)
3. ⏭️ Update EC2 IAM role with SecretsManager permissions
4. ⏭️ Update Launch Template user data with environment variables
5. ⏭️ Test locally with environment variables
6. ⏭️ Deploy to dev environment
7. ⏭️ Verify dev deployment
8. ⏭️ Deploy to prod environment
9. ⏭️ Verify prod deployment

## Support

For detailed information, see:
- [SECRETS_MANAGER.md](SECRETS_MANAGER.md) - Complete secrets manager guide
- [DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md) - Deployment and IAM setup
- [WORKFLOW_UPDATE_GUIDE.md](WORKFLOW_UPDATE_GUIDE.md) - GitHub Actions integration
- [MIGRATION_SUMMARY.md](MIGRATION_SUMMARY.md) - Implementation details
