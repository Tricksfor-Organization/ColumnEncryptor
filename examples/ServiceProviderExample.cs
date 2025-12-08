using ColumnEncryptor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ColumnEncryptor.Examples;

/// <summary>
/// Example demonstrating how to use AddColumnEncryption with IServiceProvider
/// to fetch configuration from the service collection
/// </summary>
public class ServiceProviderExample
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Example 1: Using IServiceProvider to fetch configuration from IOptions
        services.AddColumnEncryption((options, sp) =>
        {
            // Fetch application configuration from the service provider
            var appOptions = sp.GetRequiredService<IOptionsSnapshot<ApplicationOptions>>().Value;
            
            options.KeyProvider = KeyProviderType.HashiCorpVault;
            options.Vault = new VaultOptions
            {
                ServerUrl = appOptions.VaultConnectionString,
                AuthMethod = VaultAuthMethod.AppRole,
                RoleId = appOptions.VaultRoleId,
                SecretId = appOptions.VaultSecretId,
                KeysPath = "secret/encryption-keys"
            };
        });

        // Example 2: Using IServiceProvider to fetch Azure Key Vault configuration
        services.AddColumnEncryption((options, sp) =>
        {
            var azureConfig = sp.GetRequiredService<IConfiguration>();
            
            options.KeyProvider = KeyProviderType.AzureKeyVault;
            options.AzureKeyVault = new AzureKeyVaultOptions
            {
                VaultUrl = azureConfig["AzureKeyVault:VaultUrl"]!,
                AuthMethod = AzureAuthMethod.DefaultAzureCredential,
                KeyPrefix = "encryption"
            };
        });

        // Example 3: Using IServiceProvider to access other services
        services.AddColumnEncryption((options, sp) =>
        {
            var logger = sp.GetRequiredService<ILogger<ServiceProviderExample>>();
            logger.LogInformation("Configuring column encryption...");
            
            var environment = sp.GetRequiredService<IHostEnvironment>();
            
            // Use different key providers based on environment
            if (environment.IsProduction())
            {
                options.KeyProvider = KeyProviderType.AzureKeyVault;
                options.AzureKeyVault = new AzureKeyVaultOptions
                {
                    VaultUrl = "https://prod-vault.vault.azure.net/",
                    AuthMethod = AzureAuthMethod.ManagedIdentity
                };
            }
            else
            {
                options.KeyProvider = KeyProviderType.HashiCorpVault;
                options.Vault = new VaultOptions
                {
                    ServerUrl = "http://localhost:8200",
                    AuthMethod = VaultAuthMethod.Token,
                    Token = "dev-token",
                    KeysPath = "secret/encryption-keys"
                };
            }
        });
    }
}

// Example application options class
public class ApplicationOptions
{
    public string VaultConnectionString { get; set; } = string.Empty;
    public string VaultRoleId { get; set; } = string.Empty;
    public string VaultSecretId { get; set; } = string.Empty;
}
