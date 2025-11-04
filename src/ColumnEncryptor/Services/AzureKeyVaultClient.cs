using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using ColumnEncryptor.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace ColumnEncryptor.Services;

/// <summary>
/// Azure Key Vault client implementation
/// </summary>
public sealed class AzureKeyVaultClient : IVaultClient, IDisposable
{
    private readonly SecretClient _secretClient;
    private readonly ILogger<AzureKeyVaultClient> _logger;

    public AzureKeyVaultClient(
        IOptions<AzureKeyVaultOptions> options,
        ILogger<AzureKeyVaultClient> logger)
    {
        var azureOptions = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(azureOptions.VaultUrl))
        {
            throw new ArgumentException("Azure Key Vault URL must be configured", nameof(options));
        }

        // Create the appropriate credential based on authentication method
        TokenCredential credential = azureOptions.AuthMethod switch
        {
            AzureAuthMethod.DefaultAzureCredential => new DefaultAzureCredential(),
            AzureAuthMethod.ManagedIdentity => new ManagedIdentityCredential(azureOptions.ClientId),
            AzureAuthMethod.ServicePrincipal => new ClientSecretCredential(
                azureOptions.TenantId, 
                azureOptions.ClientId, 
                azureOptions.ClientSecret),
            _ => throw new NotSupportedException($"Authentication method {azureOptions.AuthMethod} is not supported")
        };

        _secretClient = new SecretClient(new Uri(azureOptions.VaultUrl), credential);
        _logger.LogDebug("Initialized Azure Key Vault client for {VaultUrl}", azureOptions.VaultUrl);
    }

    public async Task<T?> ReadSecretAsync<T>(string path) where T : class
    {
        try
        {
            var secretName = ConvertPathToSecretName(path);
            var response = await _secretClient.GetSecretAsync(secretName);
            
            if (response?.Value?.Value == null)
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(response.Value.Value);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug(ex, "Secret not found at path: {Path}", path);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read secret from Azure Key Vault at path: {Path}", path);
            throw new InvalidOperationException($"Failed to read secret from Azure Key Vault: {ex.Message}", ex);
        }
    }

    public async Task WriteSecretAsync<T>(string path, T data) where T : class
    {
        try
        {
            var secretName = ConvertPathToSecretName(path);
            var json = JsonSerializer.Serialize(data);
            
            await _secretClient.SetSecretAsync(secretName, json);
            _logger.LogDebug("Successfully wrote secret to Azure Key Vault at path: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write secret to Azure Key Vault at path: {Path}", path);
            throw new InvalidOperationException($"Failed to write secret to Azure Key Vault: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<string>> ListSecretsAsync(string path)
    {
        try
        {
            var secrets = new List<string>();
            var prefix = ConvertPathToSecretPrefix(path);
            
            await foreach (var secretProperties in _secretClient.GetPropertiesOfSecretsAsync())
            {
                if (secretProperties.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    // Convert secret name back to relative path
                    var relativeName = secretProperties.Name[prefix.Length..];
                    if (!string.IsNullOrEmpty(relativeName))
                    {
                        secrets.Add(relativeName);
                    }
                }
            }

            return secrets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list secrets from Azure Key Vault at path: {Path}", path);
            return Enumerable.Empty<string>();
        }
    }

    public async Task DeleteSecretAsync(string path)
    {
        try
        {
            var secretName = ConvertPathToSecretName(path);
            var operation = await _secretClient.StartDeleteSecretAsync(secretName);
            
            // Wait for the deletion to complete
            await operation.WaitForCompletionAsync();
            
            _logger.LogDebug("Successfully deleted secret from Azure Key Vault at path: {Path}", path);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug(ex, "Secret not found for deletion at path: {Path}", path);
            // Not an error if the secret doesn't exist
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete secret from Azure Key Vault at path: {Path}", path);
            throw new InvalidOperationException($"Failed to delete secret from Azure Key Vault: {ex.Message}", ex);
        }
    }

    private static string ConvertPathToSecretName(string path)
    {
        // Azure Key Vault secret names can only contain alphanumeric characters and hyphens
        // Convert path separators and other characters to hyphens
        return path.Replace("/", "-")
                   .Replace("\\", "-")
                   .Replace("_", "-")
                   .Replace(".", "-")
                   .ToLowerInvariant();
    }

    private static string ConvertPathToSecretPrefix(string path)
    {
        // Convert path to prefix for listing
        var prefix = ConvertPathToSecretName(path);
        return prefix.EndsWith('-') ? prefix : $"{prefix}-";
    }

    public void Dispose()
    {
        // SecretClient doesn't implement IDisposable, so nothing to dispose
        _logger.LogDebug("Azure Key Vault client disposed");
    }
}