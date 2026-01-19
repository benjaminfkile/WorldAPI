# AWS Secrets Manager Configuration

This document explains how the World API loads database credentials from AWS Secrets Manager.

## Overview

The World API no longer stores sensitive database credentials in `appsettings.json`. Instead, it fetches them from AWS Secrets Manager at startup using the AWS SDK for .NET.

## Architecture

1. **WorldDbSecrets** - POCO model that represents the secret JSON structure
2. **SecretsManagerService** - Service that fetches and deserializes secrets from AWS
3. **Program.cs** - Loads secrets at startup before DbContext registration

## Required Environment Variables

The application requires the following environment variables:

- `AWS_REGION` - AWS region where the secret is stored (e.g., `us-east-1`)
- `AWS_DB_SECRET_ARN` - ARN or name of the secret in AWS Secrets Manager

## Secret JSON Format

The secret stored in AWS Secrets Manager must have the following JSON structure:

```json
{
  "host": "your-db-host.rds.amazonaws.com",
  "database": ,
  "username": "dbuser",
  "password": "your-secure-password",
  "port": 5432
}
```

## Authentication Methods

### Local Development
Set environment variables with AWS credentials:
```bash
export AWS_REGION=us-east-1
export AWS_DB_SECRET_ARN=arn:aws:secretsmanager:us-east-1:123456789:secret:worldapi-db-dev
export AWS_ACCESS_KEY_ID=your-access-key
export AWS_SECRET_ACCESS_KEY=your-secret-key
```

Or use AWS CLI profile:
```bash
aws configure
export AWS_REGION=us-east-1
export AWS_DB_SECRET_ARN=arn:aws:secretsmanager:us-east-1:123456789:secret:worldapi-db-dev
```

### Docker on EC2 (Production)
The application uses **IAM role authentication** when running on EC2. No credentials need to be configured in the container.

1. Attach an IAM role to the EC2 instance with the following policy:
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "secretsmanager:GetSecretValue"
      ],
      "Resource": "arn:aws:secretsmanager:REGION:ACCOUNT:secret:worldapi-db-*"
    }
  ]
}
```

2. Set environment variables in your Docker container or task definition:
```bash
AWS_REGION=us-east-1
AWS_DB_SECRET_ARN=arn:aws:secretsmanager:us-east-1:123456789:secret:worldapi-db-prod
```

## Creating Secrets in AWS

### Using AWS Console
1. Navigate to AWS Secrets Manager
2. Click "Store a new secret"
3. Select "Other type of secret"
4. Add key-value pairs for: `host`, `database`, `username`, `password`, `port`
5. Name the secret (e.g., `worldapi-db-prod`)
6. Copy the ARN for use in `AWS_DB_SECRET_ARN`

### Using AWS CLI
```bash
aws secretsmanager create-secret \
  --name worldapi-db-prod \
  --description "World API database credentials" \
  --secret-string '{
    "host": "your-db.rds.amazonaws.com",
    "database": ,
    "username": "dbuser",
    "password": "your-password",
    "port": 5432
  }'
```

## Troubleshooting

### Secret Not Found
- Verify `AWS_DB_SECRET_ARN` is correct
- Check IAM permissions for the user/role
- Ensure the secret exists in the specified region

### Connection Failures
- Verify the database credentials in the secret are correct
- Check security group rules allow connection from your application
- Verify the database host is accessible from your environment

### IAM Permission Issues
Error: `User is not authorized to perform secretsmanager:GetSecretValue`
- Ensure the IAM user or EC2 instance role has the correct policy
- Verify the resource ARN matches the secret

## Logging

The application logs secret retrieval (without exposing sensitive data):
```
Successfully loaded database configuration from AWS Secrets Manager
Database host: mydb.rds.amazonaws.com, Database: worldapi
```

If there's an error, check application logs for details about the failure.
