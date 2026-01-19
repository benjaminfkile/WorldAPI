# Deployment Configuration Guide

## Overview

Now that the World API loads database credentials from AWS Secrets Manager, you need to ensure that the environment variables `AWS_REGION` and `AWS_DB_SECRET_ARN` are available to the Docker container running on EC2.

## Option 1: Using EC2 User Data (Recommended)

Configure environment variables in the EC2 Launch Template or Auto Scaling Group User Data:

```bash
#!/bin/bash

# Set environment variables for the Docker container
cat > /etc/environment << 'EOF'
AWS_REGION=us-east-1
AWS_DB_SECRET_ARN=arn:aws:secretsmanager:us-east-1:123456789012:secret:worldapi-db-prod-XYZ
EOF

# Pull and run the Docker container with environment variables
docker pull ${ECR_REGISTRY}/${ECR_REPOSITORY}:latest
docker run -d \
  --name worldapi \
  -p 3004:3004 \
  --env AWS_REGION=us-east-1 \
  --env AWS_DB_SECRET_ARN=arn:aws:secretsmanager:us-east-1:123456789012:secret:worldapi-db-prod-XYZ \
  ${ECR_REGISTRY}/${ECR_REPOSITORY}:latest
```

## Option 2: Using Docker Compose

If using Docker Compose, create an `environment` section:

```yaml
version: '3.8'
services:
  worldapi:
    image: ${ECR_REGISTRY}/${ECR_REPOSITORY}:latest
    ports:
      - "3004:3004"
    environment:
      - AWS_REGION=us-east-1
      - AWS_DB_SECRET_ARN=arn:aws:secretsmanager:us-east-1:123456789012:secret:worldapi-db-prod
    restart: unless-stopped
```

## Option 3: Using ECS Task Definition

If deploying to ECS, add environment variables to the task definition:

```json
{
  "family": ,
  "containerDefinitions": [
    {
      "name": ,
      "image": "${ECR_REGISTRY}/${ECR_REPOSITORY}:latest",
      "portMappings": [
        {
          "containerPort": 3004,
          "protocol": "tcp"
        }
      ],
      "environment": [
        {
          "name": "AWS_REGION",
          "value": "us-east-1"
        },
        {
          "name": "AWS_DB_SECRET_ARN",
          "value": "arn:aws:secretsmanager:us-east-1:123456789012:secret:worldapi-db-prod"
        }
      ]
    }
  ]
}
```

## IAM Role Configuration

### EC2 Instance Role Policy

Attach this policy to the IAM role associated with your EC2 instances:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "AllowSecretsManagerAccess",
      "Effect": "Allow",
      "Action": [
        "secretsmanager:GetSecretValue",
        "secretsmanager:DescribeSecret"
      ],
      "Resource": [
        "arn:aws:secretsmanager:us-east-1:123456789012:secret:worldapi-db-prod-*",
        "arn:aws:secretsmanager:us-east-1:123456789012:secret:worldapi-db-dev-*"
      ]
    },
    {
      "Sid": "AllowECRAccess",
      "Effect": "Allow",
      "Action": [
        "ecr:GetAuthorizationToken",
        "ecr:BatchCheckLayerAvailability",
        "ecr:GetDownloadUrlForLayer",
        "ecr:BatchGetImage"
      ],
      "Resource": "*"
    }
  ]
}
```

### Trust Relationship

Ensure the role has EC2 in its trust relationship:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Service": "ec2.amazonaws.com"
      },
      "Action": "sts:AssumeRole"
    }
  ]
}
```

## GitHub Secrets to Add

Add these secrets to your GitHub repository (Settings → Secrets and variables → Actions):

### For Dev Environment
- `AWS_DB_SECRET_ARN_DEV` - ARN of the dev database secret
  - Example: `arn:aws:secretsmanager:us-east-1:123456789012:secret:worldapi-db-dev-abc123`

### For Prod Environment
- `AWS_DB_SECRET_ARN_PROD` - ARN of the prod database secret
  - Example: `arn:aws:secretsmanager:us-east-1:123456789012:secret:worldapi-db-prod-xyz789`

### Existing (keep as-is)
- `AWS_REGION`
- `AWS_ACCESS_KEY_ID`
- `AWS_SECRET_ACCESS_KEY`
- `ECR_REGISTRY`
- `ECR_REPOSITORY`
- `ASG_NAME`

## Docker Run Command Example

When manually running the container on EC2:

```bash
docker run -d \
  --name worldapi \
  -p 3004:3004 \
  --restart unless-stopped \
  -e AWS_REGION=us-east-1 \
  -e AWS_DB_SECRET_ARN=arn:aws:secretsmanager:us-east-1:123456789012:secret:worldapi-db-prod \
  your-ecr-registry.dkr.ecr.us-east-1.amazonaws.com/worldapi:latest
```

## Verification Steps

After deployment, verify the configuration:

1. **SSH into EC2 instance**
   ```bash
   ssh ec2-user@your-instance-ip
   ```

2. **Check Docker container logs**
   ```bash
   docker logs worldapi
   ```

   Look for:
   ```
   Successfully loaded database configuration from AWS Secrets Manager
   Database host: your-db.rds.amazonaws.com, Database: worldapi
   ```

3. **Test the health endpoint**
   ```bash
   curl http://localhost:3004/health
   ```

4. **Verify IAM permissions**
   ```bash
   # From within EC2 instance
   aws secretsmanager get-secret-value \
     --secret-id your-secret-arn \
     --region us-east-1
   ```

## Rollback Plan

If issues occur after deployment:

1. **Temporarily add connection string to appsettings.json** (emergency only)
2. **Revert to previous Docker image**
   ```bash
   docker stop worldapi
   docker rm worldapi
   docker run -d --name worldapi -p 3004:3004 your-ecr:previous-tag
   ```

3. **Check CloudWatch Logs** for detailed error messages

## Common Issues

### Issue: "AWS_DB_SECRET_ARN environment variable is required"
**Solution**: Ensure the environment variable is set in your Docker run command or ECS task definition

### Issue: "Secret not found"
**Solution**: 
- Verify the ARN is correct
- Ensure the secret exists in the specified region
- Check IAM role has `secretsmanager:GetSecretValue` permission

### Issue: "User is not authorized to perform secretsmanager:GetSecretValue"
**Solution**:
- Verify EC2 instance has an IAM role attached
- Verify IAM role policy includes the correct secret ARN
- Wait 5-10 minutes for IAM changes to propagate

### Issue: Container starts but can't connect to database
**Solution**:
- Verify the secret contains correct database credentials
- Check RDS security group allows connections from EC2
- Verify RDS endpoint is accessible from the EC2 instance

## Testing Locally Before Deployment

Test the changes locally before deploying:

```bash
# Set environment variables
export AWS_REGION=us-east-1
export AWS_DB_SECRET_ARN=arn:aws:secretsmanager:us-east-1:123456789012:secret:worldapi-db-dev

# Run the application
cd src/WorldApi
dotnet run
```

Or test with Docker locally:

```bash
# Build the image
docker build -t worldapi-test .

# Run with environment variables
docker run -p 3004:3004 \
  -e AWS_REGION=us-east-1 \
  -e AWS_DB_SECRET_ARN=arn:aws:secretsmanager:us-east-1:123456789012:secret:worldapi-db-dev \
  -e AWS_ACCESS_KEY_ID=your-key \
  -e AWS_SECRET_ACCESS_KEY=your-secret \
  worldapi-test
```
