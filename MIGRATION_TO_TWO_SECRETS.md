# Migration Guide: Single-Secret to Two-Secret Architecture

## Overview

If you previously deployed the World API with a single combined secret, this guide explains how to migrate to the new two-secret architecture.

## Why Two Secrets?

The two-secret design provides:
- **Clear Ownership**: Infrastructure team (AWS) vs. Application team
- **Independent Rotation**: Change credentials without modifying config
- **Better Security**: Reduces blast radius of credential compromise
- **Easier Extension**: Add application settings without touching credentials

## Migration Steps

### Phase 1: Create New Secrets (Non-Breaking)

#### Step 1a: Create RDS-Managed Secret

If using Amazon RDS, you likely already have this. Find it in Secrets Manager:

```bash
aws secretsmanager list-secrets \
  --filters Key=name,Values=rds \
  --region us-east-1
```

Or create manually:

```bash
aws secretsmanager create-secret \
  --name worldapi-rds-credentials-prod \
  --description "RDS database credentials for World API" \
  --secret-string '{
    "username": "postgres",
    "password": "YOUR_DB_PASSWORD",
    "engine": "postgres",
    "host": "your-db.rds.amazonaws.com",
    "port": 5432,
    "dbname": "postgres"
  }' \
  --region us-east-1
```

**Copy the ARN:**
```bash
aws secretsmanager describe-secret \
  --secret-id worldapi-rds-credentials-prod \
  --region us-east-1 \
  --query 'ARN' \
  --output text
```

#### Step 1b: Create Application-Managed Secret

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

**Copy the ARN:**
```bash
aws secretsmanager describe-secret \
  --secret-id worldapi-app-config-prod \
  --region us-east-1 \
  --query 'ARN' \
  --output text
```

### Phase 2: Update GitHub Secrets

Add new GitHub Secrets (Settings → Secrets and variables → Actions):

**For Dev:**
- `AWS_RDS_SECRET_ARN_DEV` → ARN from Step 1a
- `AWS_APP_SECRET_ARN_DEV` → ARN from Step 1b

**For Prod:**
- `AWS_RDS_SECRET_ARN_PROD` → ARN from Step 1a
- `AWS_APP_SECRET_ARN_PROD` → ARN from Step 1b

**Keep existing:**
- `AWS_REGION`
- `AWS_ACCESS_KEY_ID`
- `AWS_SECRET_ACCESS_KEY`

### Phase 3: Update Deployment Infrastructure

#### Update EC2 IAM Role

Add this policy:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["secretsmanager:GetSecretValue"],
      "Resource": [
        "arn:aws:secretsmanager:us-east-1:*:secret:worldapi-rds-credentials-*",
        "arn:aws:secretsmanager:us-east-1:*:secret:worldapi-app-config-*"
      ]
    }
  ]
}
```

#### Update Launch Templates

For each environment (dev/prod), update the **User Data** to pass new environment variables:

**Old (single secret):**
```bash
docker run -d \
  --name worldapi \
  -p 3004:3004 \
  -e AWS_REGION=us-east-1 \
  -e AWS_DB_SECRET_ARN=arn:aws:secretsmanager:... \
  your-ecr:latest
```

**New (two secrets):**
```bash
docker run -d \
  --name worldapi \
  -p 3004:3004 \
  -e AWS_REGION=us-east-1 \
  -e AWS_RDS_SECRET_ARN=arn:aws:secretsmanager:us-east-1:ACCOUNT:secret:worldapi-rds-credentials-prod \
  -e AWS_APP_SECRET_ARN=arn:aws:secretsmanager:us-east-1:ACCOUNT:secret:worldapi-app-config-prod \
  your-ecr:latest
```

### Phase 4: Test Locally

Before deploying to production:

```bash
# Set local environment variables
export AWS_REGION=us-east-1
export AWS_RDS_SECRET_ARN=arn:aws:secretsmanager:us-east-1:ACCOUNT:secret:worldapi-rds-credentials-dev
export AWS_APP_SECRET_ARN=arn:aws:secretsmanager:us-east-1:ACCOUNT:secret:worldapi-app-config-dev

# Configure AWS credentials
aws configure

# Test the application
cd src/WorldApi
dotnet run
```

Expected output:
```
Successfully loaded secrets from AWS Secrets Manager
RDS secret: host=localhost, port=5432
App secret: database=worldapi, worldVersion=world-v1
```

### Phase 5: Deploy

#### Deploy to Dev

1. **Merge changes to `dev` branch**
2. **Wait for GitHub Actions to complete**
3. **Verify in logs:**
   ```bash
   ssh ec2-user@dev-instance-ip
   docker logs worldapi
   ```
4. **Test API endpoints**
   ```bash
   curl https://dev-api.yourdomain.com/health
   ```

#### Deploy to Prod (After Dev Verification)

1. **Merge changes to `main` branch**
2. **Wait for GitHub Actions to complete**
3. **Monitor CloudWatch Logs**
4. **Verify no increased error rates**
5. **Test critical API endpoints**

### Phase 6: Cleanup (After 1-2 Weeks)

Once confident in the two-secret architecture:

1. **Delete old single-combined secret**
   ```bash
   aws secretsmanager delete-secret \
     --secret-id worldapi-db-combined-prod \
     --force-delete-without-recovery \
     --region us-east-1
   ```

2. **Remove old GitHub Secrets**
   - Delete `AWS_DB_SECRET_ARN_DEV`
   - Delete `AWS_DB_SECRET_ARN_PROD`

3. **Update documentation** to reference new secrets only

## Rollback Plan

If issues arise during migration:

### Immediate Rollback (Emergency)

1. **SSH into EC2 instance**
   ```bash
   ssh ec2-user@prod-instance-ip
   ```

2. **Stop and remove new container**
   ```bash
   docker stop worldapi && docker rm worldapi
   ```

3. **Run previous version**
   ```bash
   docker run -d \
     --name worldapi \
     -p 3004:3004 \
     -e AWS_REGION=us-east-1 \
     -e AWS_DB_SECRET_ARN=arn:aws:secretsmanager:REGION:ACCOUNT:secret:worldapi-db-combined \
     your-ecr:previous-tag
   ```

### Full Rollback

1. **Revert Launch Template** to previous version
2. **Terminate new EC2 instances**
   ```bash
   aws autoscaling terminate-instance-in-auto-scaling-group \
     --instance-id i-xxxxx \
     --auto-scaling-group-name worldapi-asg \
     --should-decrement-desired-capacity
   ```

3. **Wait for old instances to spawn** (Auto Scaling Group will replace them)

## Verification Checklist

Before considering migration complete:

- [ ] Dev environment running on two-secret architecture
- [ ] Dev API responding correctly
- [ ] Dev database connectivity verified
- [ ] Prod environment running on two-secret architecture
- [ ] Prod API responding correctly
- [ ] Prod database connectivity verified
- [ ] No increased error rates in CloudWatch
- [ ] No security alerts triggered
- [ ] Application logs show both secrets loaded successfully
- [ ] Old single-secret still exists (as backup) for 1-2 weeks
- [ ] Team documentation updated with new architecture
- [ ] Runbook updated with new ARN environment variables

## Common Issues

### Issue: "AWS_RDS_SECRET_ARN environment variable is required"

**Cause:** Missing environment variable in Docker launch command  
**Solution:** Verify Launch Template User Data includes `-e AWS_RDS_SECRET_ARN=...`

### Issue: "AWS_APP_SECRET_ARN environment variable is required"

**Cause:** Missing environment variable in Docker launch command  
**Solution:** Verify Launch Template User Data includes `-e AWS_APP_SECRET_ARN=...`

### Issue: "RDS secret not found"

**Cause:** Wrong ARN or missing secret  
**Solution:**
```bash
aws secretsmanager describe-secret \
  --secret-id worldapi-rds-credentials-prod \
  --region us-east-1
```

### Issue: "Application secret not found"

**Cause:** Wrong ARN or missing secret  
**Solution:**
```bash
aws secretsmanager describe-secret \
  --secret-id worldapi-app-config-prod \
  --region us-east-1
```

### Issue: "User is not authorized to perform: secretsmanager:GetSecretValue"

**Cause:** IAM role missing permissions  
**Solution:** Add policy from Phase 3 to EC2 instance role and wait 5-10 minutes for propagation

## Timeline

- **Day 1**: Create new secrets, update IAM role
- **Day 2**: Test locally with new secrets
- **Day 3**: Deploy to dev and verify (24+ hours monitoring)
- **Day 4**: Deploy to prod and verify (24+ hours monitoring)
- **Day 14**: Delete old combined secret after confidence period

## Documentation References

- [TWO_SECRET_ARCHITECTURE.md](TWO_SECRET_ARCHITECTURE.md) - Architecture overview
- [DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md) - Detailed deployment steps
- [DEPLOYMENT_CHECKLIST.md](DEPLOYMENT_CHECKLIST.md) - Step-by-step verification checklist
- [QUICK_START.md](QUICK_START.md) - Quick reference guide

## Support

For questions or issues:
1. Check logs: `docker logs worldapi`
2. Review troubleshooting sections above
3. Consult architecture documentation
4. Reach out to the infrastructure team
