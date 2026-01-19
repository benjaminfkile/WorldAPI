using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using System.Text.Json;

namespace WorldApi.Configuration;

/// <summary>
/// Service for fetching and deserializing secrets from AWS Secrets Manager.
/// Manages two separate secrets:
/// 1. RDS-managed secret (infrastructure credentials: username, password)
/// 2. Application-managed secret (application configuration: database, host, port, worldVersion)
/// 
/// Uses IAM role authentication when running on EC2, or environment credentials for local development.
/// </summary>
public class SecretsManagerService
{
    private readonly IAmazonSecretsManager _secretsManager;
    private readonly ILogger<SecretsManagerService> _logger;

    public SecretsManagerService(IAmazonSecretsManager secretsManager, ILogger<SecretsManagerService> logger)
    {
        _secretsManager = secretsManager;
        _logger = logger;
    }

    /// <summary>
    /// Fetches the RDS-managed database secret from AWS Secrets Manager.
    /// This secret is created and rotated by Amazon RDS - contains username and password only.
    /// </summary>
    /// <param name="secretArn">The ARN of the RDS secret</param>
    /// <returns>Deserialized RdsDbSecrets object containing username and password</returns>
    /// <exception cref="InvalidOperationException">Thrown when secret cannot be fetched or deserialized</exception>
    public async Task<RdsDbSecrets> GetRdsDbSecretsAsync(string secretArn)
    {
        try
        {
            // _logger.LogInformation("Fetching RDS database secrets from AWS Secrets Manager: {SecretArn}", secretArn);

            var request = new GetSecretValueRequest
            {
                SecretId = secretArn
            };

            var response = await _secretsManager.GetSecretValueAsync(request);

            if (string.IsNullOrEmpty(response.SecretString))
            {
                throw new InvalidOperationException("RDS secret value is empty or null");
            }

            // _logger.LogInformation("Successfully fetched RDS database secrets");

            var secrets = JsonSerializer.Deserialize<RdsDbSecrets>(response.SecretString, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (secrets == null)
            {
                throw new InvalidOperationException("Failed to deserialize secrets into RdsDbSecrets");
            }

            // Validate required RDS secret fields
            if (string.IsNullOrEmpty(secrets.Username) || 
                string.IsNullOrEmpty(secrets.Password))
            {
                throw new InvalidOperationException("RDS secret is missing required fields (username or password)");
            }

            return secrets;
        }
        catch (ResourceNotFoundException ex)
        {
            _logger.LogError(ex, "RDS secret not found: {SecretArn}", secretArn);
            throw new InvalidOperationException($"RDS secret not found: {secretArn}", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize RDS secret JSON");
            throw new InvalidOperationException("Failed to deserialize RDS secret JSON. Ensure the secret matches the expected format.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching RDS secrets from AWS Secrets Manager");
            throw new InvalidOperationException("Failed to fetch RDS secrets from AWS Secrets Manager", ex);
        }
    }

    /// <summary>
    /// Fetches the application-managed secret from AWS Secrets Manager.
    /// This secret contains application configuration: database name, host, port, and world settings.
    /// </summary>
    /// <param name="secretArn">The ARN of the application secret</param>
    /// <returns>Deserialized WorldAppSecrets object</returns>
    /// <exception cref="InvalidOperationException">Thrown when secret cannot be fetched or deserialized</exception>
    public async Task<WorldAppSecrets> GetWorldAppSecretsAsync(string secretArn)
    {
        try
        {
            // _logger.LogInformation("Fetching application secrets from AWS Secrets Manager: {SecretArn}", secretArn);

            var request = new GetSecretValueRequest
            {
                SecretId = secretArn
            };

            var response = await _secretsManager.GetSecretValueAsync(request);

            if (string.IsNullOrEmpty(response.SecretString))
            {
                throw new InvalidOperationException("Application secret value is empty or null");
            }

            // _logger.LogInformation("Successfully fetched application secrets");

            // _logger.LogInformation("Raw secret JSON: {SecretJson}", response.SecretString);

            var secrets = JsonSerializer.Deserialize<WorldAppSecrets>(response.SecretString, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (secrets == null)
            {
                throw new InvalidOperationException("Failed to deserialize secrets into WorldAppSecrets");
            }

            // _logger.LogInformation("Deserialized CloudfrontUrl: {CloudfrontUrl}", secrets.CloudfrontUrl ?? "(null)");

            // Validate required application secret fields
            if (string.IsNullOrEmpty(secrets.Database) || 
                string.IsNullOrEmpty(secrets.DbHost))
            {
                throw new InvalidOperationException("Application secret is missing required fields (database and dbHost)");
            }

            return secrets;
        }
        catch (ResourceNotFoundException ex)
        {
            _logger.LogError(ex, "Application secret not found: {SecretArn}", secretArn);
            throw new InvalidOperationException($"Application secret not found: {secretArn}", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize application secret JSON");
            throw new InvalidOperationException("Failed to deserialize application secret JSON. Ensure the secret matches the expected format.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching application secrets from AWS Secrets Manager");
            throw new InvalidOperationException("Failed to fetch application secrets from AWS Secrets Manager", ex);
        }
    }
}
