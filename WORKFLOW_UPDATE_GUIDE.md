# GitHub Actions Workflow Update (Optional Enhancement)

## Current State

The current deployment workflow deploys to either `dev` or `prod` environment based on the branch, but doesn't currently pass environment variables to the EC2 instances during the Auto Scaling Group refresh.

## Recommended Approach

Since the Auto Scaling Group refresh doesn't directly configure Docker container environment variables, you have two main options:

### Option 1: Configure in Launch Template User Data (Recommended)

Update your EC2 Launch Template's User Data to include environment variables. This is the most common approach for Auto Scaling Groups.

**Steps:**
1. Go to EC2 → Launch Templates
2. Create a new version of your launch template
3. Update the User Data with:

```bash
#!/bin/bash

# Pull latest image from ECR
aws ecr get-login-password --region ${AWS_REGION} | docker login --username AWS --password-stdin ${ECR_REGISTRY}
docker pull ${ECR_REGISTRY}/${ECR_REPOSITORY}:latest

# Stop and remove existing container
docker stop worldapi 2>/dev/null || true
docker rm worldapi 2>/dev/null || true

# Run new container with environment variables
docker run -d \
  --name worldapi \
  -p 3004:3004 \
  --restart unless-stopped \
  -e AWS_REGION=us-east-1 \
  -e AWS_DB_SECRET_ARN=arn:aws:secretsmanager:us-east-1:ACCOUNT:secret:worldapi-db-prod \
  ${ECR_REGISTRY}/${ECR_REPOSITORY}:latest
```

4. Update the Auto Scaling Group to use the new launch template version
5. Set the launch template as default

**Separate Launch Templates for Dev/Prod:**
- Create `worldapi-launch-template-dev` with dev secret ARN
- Create `worldapi-launch-template-prod` with prod secret ARN
- Use separate Auto Scaling Groups for dev and prod

### Option 2: Enhanced Workflow with Systems Manager Parameter Store

Store configuration in AWS Systems Manager Parameter Store and reference it in User Data:

**1. Create SSM Parameters:**
```bash
# Dev environment
aws ssm put-parameter \
  --name /worldapi/dev/db-secret-arn \
  --value "arn:aws:secretsmanager:us-east-1:123456789012:secret:worldapi-db-dev" \
  --type String

# Prod environment
aws ssm put-parameter \
  --name /worldapi/prod/db-secret-arn \
  --value "arn:aws:secretsmanager:us-east-1:123456789012:secret:worldapi-db-prod" \
  --type String
```

**2. Update Launch Template User Data:**
```bash
#!/bin/bash

# Determine environment from EC2 tag or instance metadata
ENVIRONMENT=$(aws ec2 describe-tags \
  --region us-east-1 \
  --filters "Name=resource-id,Values=$(ec2-metadata --instance-id | cut -d ' ' -f 2)" \
  "Name=key,Values=Environment" \
  --query 'Tags[0].Value' \
  --output text)

# Get configuration from SSM Parameter Store
AWS_DB_SECRET_ARN=$(aws ssm get-parameter \
  --region us-east-1 \
  --name "/worldapi/${ENVIRONMENT}/db-secret-arn" \
  --query 'Parameter.Value' \
  --output text)

# Pull and run container
aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin ${ECR_REGISTRY}
docker pull ${ECR_REGISTRY}/${ECR_REPOSITORY}:latest

docker stop worldapi 2>/dev/null || true
docker rm worldapi 2>/dev/null || true

docker run -d \
  --name worldapi \
  -p 3004:3004 \
  --restart unless-stopped \
  -e AWS_REGION=us-east-1 \
  -e AWS_DB_SECRET_ARN="${AWS_DB_SECRET_ARN}" \
  ${ECR_REGISTRY}/${ECR_REPOSITORY}:latest
```

**3. Add IAM Permissions:**
```json
{
  "Effect": "Allow",
  "Action": [
    "ssm:GetParameter",
    "ssm:GetParameters"
  ],
  "Resource": "arn:aws:ssm:us-east-1:123456789012:parameter/worldapi/*"
}
```

### Option 3: Environment File on EC2

Store an environment file on the EC2 instance and mount it:

**1. Create environment file during instance initialization:**
```bash
#!/bin/bash

# Create environment file
cat > /opt/worldapi/.env << EOF
AWS_REGION=us-east-1
AWS_DB_SECRET_ARN=arn:aws:secretsmanager:us-east-1:123456789012:secret:worldapi-db-prod
EOF

# Run container with env file
docker run -d \
  --name worldapi \
  -p 3004:3004 \
  --restart unless-stopped \
  --env-file /opt/worldapi/.env \
  ${ECR_REGISTRY}/${ECR_REPOSITORY}:latest
```

## Workflow Enhancement (Optional)

If you want to manage this through the workflow, you could add a step to update the Launch Template:

```yaml
- name: Update Launch Template User Data
  run: |
    # Get current launch template
    TEMPLATE_ID=$(aws ec2 describe-launch-templates \
      --launch-template-names worldapi-${{ github.ref_name }} \
      --query 'LaunchTemplates[0].LaunchTemplateId' \
      --output text)
    
    # Create user data script
    cat > user-data.sh << 'SCRIPT'
    #!/bin/bash
    aws ecr get-login-password --region ${{ secrets.AWS_REGION }} | docker login --username AWS --password-stdin ${{ secrets.ECR_REGISTRY }}
    docker pull ${{ secrets.ECR_REGISTRY }}/${{ secrets.ECR_REPOSITORY }}:latest
    docker stop worldapi || true
    docker rm worldapi || true
    docker run -d \
      --name worldapi \
      -p 3004:3004 \
      --restart unless-stopped \
      -e AWS_REGION=${{ secrets.AWS_REGION }} \
      -e AWS_DB_SECRET_ARN=${{ secrets.AWS_DB_SECRET_ARN }} \
      ${{ secrets.ECR_REGISTRY }}/${{ secrets.ECR_REPOSITORY }}:latest
    SCRIPT
    
    # Base64 encode user data
    USER_DATA=$(cat user-data.sh | base64 -w 0)
    
    # Create new launch template version
    aws ec2 create-launch-template-version \
      --launch-template-id $TEMPLATE_ID \
      --source-version '$Latest' \
      --launch-template-data "{\"UserData\":\"$USER_DATA\"}"
```

## Recommended Setup Summary

**For simplicity and reliability:**

1. ✅ Use **Launch Template User Data** (Option 1)
2. ✅ Create separate Launch Templates for dev and prod
3. ✅ Hardcode environment-specific secrets in each template's user data
4. ✅ Let the Auto Scaling Group refresh pick up the new Docker image automatically

**What the workflow handles:**
- ✅ Building and pushing Docker images
- ✅ Triggering Auto Scaling Group refresh

**What the Launch Template handles:**
- ✅ Pulling the latest Docker image
- ✅ Setting environment variables
- ✅ Starting the container

This separation of concerns is cleaner and more maintainable than trying to do everything in the GitHub Actions workflow.

## Required IAM Policy Updates

Add to your EC2 Instance Role:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "AllowSecretsManager",
      "Effect": "Allow",
      "Action": [
        "secretsmanager:GetSecretValue"
      ],
      "Resource": [
        "arn:aws:secretsmanager:*:*:secret:worldapi-db-*"
      ]
    },
    {
      "Sid": "AllowECR",
      "Effect": "Allow",
      "Action": [
        "ecr:GetAuthorizationToken",
        "ecr:BatchCheckLayerAvailability",
        "ecr:GetDownloadUrlForLayer",
        "ecr:BatchGetImage"
      ],
      "Resource": "*"
    },
    {
      "Sid": "AllowDescribeTags",
      "Effect": "Allow",
      "Action": [
        "ec2:DescribeTags"
      ],
      "Resource": "*"
    }
  ]
}
```
