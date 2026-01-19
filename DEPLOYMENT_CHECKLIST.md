# Pre-Deployment Checklist

Use this checklist to ensure everything is configured correctly before deploying.

## AWS Resources Setup

### Development Environment

- [ ] **Create Dev Secret in AWS Secrets Manager**
  ```bash
  aws secretsmanager create-secret \
    --name worldapi-db-dev \
    --description "World API database credentials for development" \
    --secret-string '{
      "host": "your-dev-db.rds.amazonaws.com",
      "database": ,
      "username": "devuser",
      "password": "dev-password",
      "port": 5432
    }' \
    --region us-east-1
  ```

- [ ] **Copy Dev Secret ARN**
  ```bash
  aws secretsmanager describe-secret \
    --secret-id worldapi-db-dev \
    --region us-east-1 \
    --query 'ARN' \
    --output text
  ```
  ARN: `_______________________`

### Production Environment

- [ ] **Create Prod Secret in AWS Secrets Manager**
  ```bash
  aws secretsmanager create-secret \
    --name worldapi-db-prod \
    --description "World API database credentials for production" \
    --secret-string '{
      "host": "your-prod-db.rds.amazonaws.com",
      "database": ,
      "username": "produser",
      "password": "secure-prod-password",
      "port": 5432
    }' \
    --region us-east-1
  ```

- [ ] **Copy Prod Secret ARN**
  ```bash
  aws secretsmanager describe-secret \
    --secret-id worldapi-db-prod \
    --region us-east-1 \
    --query 'ARN' \
    --output text
  ```
  ARN: `_______________________`

## IAM Configuration

- [ ] **Update EC2 Instance Role Policy**
  
  Add this policy to the IAM role attached to your EC2 instances:
  
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
          "arn:aws:secretsmanager:us-east-1:*:secret:worldapi-db-dev-*",
          "arn:aws:secretsmanager:us-east-1:*:secret:worldapi-db-prod-*"
        ]
      }
    ]
  }
  ```

- [ ] **Verify IAM Role is attached to EC2 instances**
  ```bash
  aws ec2 describe-instances \
    --instance-ids i-your-instance-id \
    --query 'Reservations[0].Instances[0].IamInstanceProfile.Arn'
  ```

- [ ] **Test IAM permissions from EC2**
  ```bash
  # SSH into EC2 instance
  aws secretsmanager get-secret-value \
    --secret-id worldapi-db-prod \
    --region us-east-1
  ```

## Launch Template Configuration

### Dev Environment

- [ ] **Update Dev Launch Template User Data**
  
  Add to User Data script:
  ```bash
  docker run -d \
    --name worldapi \
    -p 3004:3004 \
    --restart unless-stopped \
    -e AWS_REGION=us-east-1 \
    -e AWS_DB_SECRET_ARN=<YOUR_DEV_SECRET_ARN> \
    ${ECR_REGISTRY}/${ECR_REPOSITORY}:latest
  ```

- [ ] **Set new version as default**

### Prod Environment

- [ ] **Update Prod Launch Template User Data**
  
  Add to User Data script:
  ```bash
  docker run -d \
    --name worldapi \
    -p 3004:3004 \
    --restart unless-stopped \
    -e AWS_REGION=us-east-1 \
    -e AWS_DB_SECRET_ARN=<YOUR_PROD_SECRET_ARN> \
    ${ECR_REGISTRY}/${ECR_REPOSITORY}:latest
  ```

- [ ] **Set new version as default**

## Local Testing

- [ ] **Set local environment variables**
  ```bash
  export AWS_REGION=us-east-1
  export AWS_DB_SECRET_ARN=<YOUR_DEV_SECRET_ARN>
  ```

- [ ] **Configure AWS credentials**
  ```bash
  aws configure
  # OR
  export AWS_ACCESS_KEY_ID=your-key
  export AWS_SECRET_ACCESS_KEY=your-secret
  ```

- [ ] **Test application startup locally**
  ```bash
  cd src/WorldApi
  dotnet run
  ```

- [ ] **Verify logs show successful secret loading**
  ```
  Successfully loaded database configuration from AWS Secrets Manager
  Database host: your-db, Database: worldapi
  ```

- [ ] **Test database connectivity**
  ```bash
  curl http://localhost:5000/health
  ```

## GitHub Configuration (Optional)

- [ ] **Add GitHub Secrets** (if managing via workflow)
  - `AWS_DB_SECRET_ARN_DEV`
  - `AWS_DB_SECRET_ARN_PROD`

## Deployment

### Dev Deployment

- [ ] **Merge changes to `dev` branch**
- [ ] **Wait for GitHub Actions workflow to complete**
- [ ] **Verify ASG refresh completes**
- [ ] **Check application logs on new EC2 instance**
  ```bash
  ssh ec2-user@dev-instance-ip
  docker logs worldapi
  ```
- [ ] **Test dev API endpoint**
  ```bash
  curl https://dev-api.yourdomain.com/health
  ```

### Prod Deployment

- [ ] **Merge changes to `main` branch**
- [ ] **Wait for GitHub Actions workflow to complete**
- [ ] **Verify ASG refresh completes**
- [ ] **Check application logs on new EC2 instance**
  ```bash
  ssh ec2-user@prod-instance-ip
  docker logs worldapi
  ```
- [ ] **Test prod API endpoint**
  ```bash
  curl https://api.yourdomain.com/health
  ```

## Verification

- [ ] **Dev: Secrets loaded successfully**
- [ ] **Dev: Database connection working**
- [ ] **Dev: API endpoints responding**
- [ ] **Prod: Secrets loaded successfully**
- [ ] **Prod: Database connection working**
- [ ] **Prod: API endpoints responding**

## Post-Deployment

- [ ] **Monitor application logs for 24 hours**
- [ ] **Verify no error spikes in CloudWatch**
- [ ] **Test all critical API endpoints**
- [ ] **Update team documentation**
- [ ] **Schedule secret rotation**

## Rollback Plan (If Needed)

- [ ] **Keep previous Docker image tag handy**
- [ ] **Document rollback steps**:
  ```bash
  # Stop current container
  docker stop worldapi && docker rm worldapi
  
  # Run previous version
  docker run -d --name worldapi -p 3004:3004 \
    ${ECR_REGISTRY}/${ECR_REPOSITORY}:previous-tag
  ```

## Notes

Record any issues or observations during deployment:

```
Date: _______________
Deployed by: _______________

Notes:




```

---

**Status**: Ready for deployment once all checkboxes are completed.
