using ColumnEncryptor.Common;
using ColumnEncryptor.Interfaces;
using ColumnEncryptor.Providers;
using ColumnEncryptor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace ColumnEncryptor;

public static class ColumnEncryptorConfiguration
{
    /// <summary>
    /// Adds column encryption services to the DI container with explicit options
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="options">Encryption configuration options</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddColumnEncryptor(
        this IServiceCollection services, 
        EncryptionOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        services.AddSingleton(options);
        services.AddSingleton<IEncryptionService>(provider => 
        {
            var keyProvider = provider.GetRequiredService<IKeyProvider>();
            return new AesGcmEncryptionService(keyProvider);
        });

        // Configure key provider based on the selected type
        switch (options.KeyProvider)
        {
            case KeyProviderType.HashiCorpVault:
                AddHashiCorpVaultKeyProvider(services, options);
                break;
            case KeyProviderType.AzureKeyVault:
                AddAzureKeyVaultKeyProvider(services, options);
                break;
            default:
                throw new NotSupportedException($"Key provider type {options.KeyProvider} is not supported");
        }

        return services;
    }

    /// <summary>
    /// Adds column encryption services with a custom configuration action
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddColumnEncryption(
        this IServiceCollection services,
        Action<EncryptionOptions> configure)
    {
        var options = new EncryptionOptions();
        configure(options);
        return services.AddColumnEncryptor(options);
    }

    /// <summary>
    /// Adds column encryption services with a custom configuration action that provides access to IServiceProvider
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configure">Configuration action with access to service provider</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddColumnEncryption(
        this IServiceCollection services,
        Action<EncryptionOptions, IServiceProvider> configure)
    {
        // Register a placeholder that will be replaced after building the service provider
        services.AddSingleton<EncryptionOptions>(sp =>
        {
            var options = new EncryptionOptions();
            configure(options, sp);
            return options;
        });

        services.AddSingleton<IEncryptionService>(provider =>
        {
            var keyProvider = provider.GetRequiredService<IKeyProvider>();
            return new AesGcmEncryptionService(keyProvider);
        });

        // Register key provider factory that will be resolved after options are configured
        services.AddSingleton<IKeyProvider>(provider =>
        {
            var options = provider.GetRequiredService<EncryptionOptions>();
            
            // Configure key provider based on the selected type
            return options.KeyProvider switch
            {
                KeyProviderType.HashiCorpVault => CreateHashiCorpVaultKeyProvider(provider, options),
                KeyProviderType.AzureKeyVault => CreateAzureKeyVaultKeyProvider(provider, options),
                _ => throw new NotSupportedException($"Key provider type {options.KeyProvider} is not supported")
            };
        });

        return services;
    }

    /// <summary>
    /// Initializes the encryption key store with a primary key if none exists
    /// Call this after building the service provider, typically in Program.cs or Startup.cs
    /// </summary>
    /// <param name="serviceProvider">Service provider</param>
    /// <param name="keyId">Optional specific key ID to create</param>
    /// <returns>Task</returns>
    public static Task InitializeEncryptionKeysAsync(
        this IServiceProvider serviceProvider, 
        string? keyId = null)
    {
        var keyProvider = serviceProvider.GetRequiredService<IKeyProvider>();
        
        if (!keyProvider.GetAllKeys().Any())
        {
            // Create a new random key (32 bytes = 256-bit AES key)
            var keyBytes = RandomNumberGenerator.GetBytes(32);
            var id = keyId ?? Guid.NewGuid().ToString("N");
            var key = new EncryptionKey(id, keyBytes, DateTime.UtcNow);
            
            keyProvider.AddKey(key);
            
            var logger = serviceProvider.GetService<ILogger<EncryptionOptions>>();
            logger?.LogInformation("Initialized encryption with new primary key: {KeyId}", id);
        }
        
        return Task.CompletedTask;
    }

    private static void AddHashiCorpVaultKeyProvider(IServiceCollection services, EncryptionOptions options)
    {
        if (options.Vault == null)
        {
            throw new InvalidOperationException("VaultOptions must be configured when using HashiCorp Vault key provider");
        }

        // Configure Vault options
        services.Configure<VaultOptions>(vaultOptions =>
        {
            vaultOptions.ServerUrl = options.Vault.ServerUrl;
            vaultOptions.AuthMethod = options.Vault.AuthMethod;
            vaultOptions.Token = options.Vault.Token;
            vaultOptions.RoleId = options.Vault.RoleId;
            vaultOptions.SecretId = options.Vault.SecretId;
            vaultOptions.KeysPath = options.Vault.KeysPath;
            vaultOptions.Namespace = options.Vault.Namespace;
            vaultOptions.CacheExpiryMinutes = options.Vault.CacheExpiryMinutes;
        });

        // Register HTTP client and Vault client manually (without AddHttpClient extension)
        services.AddSingleton<HttpClient>(provider =>
        {
            var vaultOptions = provider.GetRequiredService<IOptions<VaultOptions>>().Value;
            var client = new HttpClient
            {
                BaseAddress = new Uri(vaultOptions.ServerUrl.TrimEnd('/')),
                Timeout = TimeSpan.FromSeconds(30)
            };
            return client;
        });

        // Register HashiCorp Vault client and key provider
        services.AddSingleton<IVaultClient, HashiCorpVaultClient>();
        services.AddSingleton<IKeyProvider, VaultKeyProvider>();
    }

    private static IKeyProvider CreateHashiCorpVaultKeyProvider(IServiceProvider provider, EncryptionOptions options)
    {
        if (options.Vault == null)
        {
            throw new InvalidOperationException("VaultOptions must be configured when using HashiCorp Vault key provider");
        }

        // Create VaultOptions
        var vaultOptions = Options.Create(new VaultOptions
        {
            ServerUrl = options.Vault.ServerUrl,
            AuthMethod = options.Vault.AuthMethod,
            Token = options.Vault.Token,
            RoleId = options.Vault.RoleId,
            SecretId = options.Vault.SecretId,
            KeysPath = options.Vault.KeysPath,
            Namespace = options.Vault.Namespace,
            CacheExpiryMinutes = options.Vault.CacheExpiryMinutes
        });

        // Create HTTP client
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(vaultOptions.Value.ServerUrl.TrimEnd('/')),
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Create Vault client
        var vaultClient = new HashiCorpVaultClient(httpClient, vaultOptions, provider.GetRequiredService<ILogger<HashiCorpVaultClient>>());
        
        // Create and return key provider
        return new VaultKeyProvider(vaultClient, vaultOptions, provider.GetRequiredService<ILogger<VaultKeyProvider>>());
    }

    private static void AddAzureKeyVaultKeyProvider(IServiceCollection services, EncryptionOptions options)
    {
        if (options.AzureKeyVault == null)
        {
            throw new InvalidOperationException("AzureKeyVaultOptions must be configured when using Azure Key Vault key provider");
        }

        // Configure Azure Key Vault options
        services.Configure<AzureKeyVaultOptions>(azureOptions =>
        {
            azureOptions.VaultUrl = options.AzureKeyVault.VaultUrl;
            azureOptions.AuthMethod = options.AzureKeyVault.AuthMethod;
            azureOptions.TenantId = options.AzureKeyVault.TenantId;
            azureOptions.ClientId = options.AzureKeyVault.ClientId;
            azureOptions.ClientSecret = options.AzureKeyVault.ClientSecret;
            azureOptions.KeyPrefix = options.AzureKeyVault.KeyPrefix;
            azureOptions.CacheExpiryMinutes = options.AzureKeyVault.CacheExpiryMinutes;
        });

        // Register Azure Key Vault client and key provider
        services.AddSingleton<IVaultClient, AzureKeyVaultClient>();
        services.AddSingleton<IKeyProvider, AzureKeyVaultProvider>();
    }

    private static IKeyProvider CreateAzureKeyVaultKeyProvider(IServiceProvider provider, EncryptionOptions options)
    {
        if (options.AzureKeyVault == null)
        {
            throw new InvalidOperationException("AzureKeyVaultOptions must be configured when using Azure Key Vault key provider");
        }

        // Create AzureKeyVaultOptions
        var azureOptions = Options.Create(new AzureKeyVaultOptions
        {
            VaultUrl = options.AzureKeyVault.VaultUrl,
            AuthMethod = options.AzureKeyVault.AuthMethod,
            TenantId = options.AzureKeyVault.TenantId,
            ClientId = options.AzureKeyVault.ClientId,
            ClientSecret = options.AzureKeyVault.ClientSecret,
            KeyPrefix = options.AzureKeyVault.KeyPrefix,
            CacheExpiryMinutes = options.AzureKeyVault.CacheExpiryMinutes
        });

        // Create Azure Key Vault client
        var vaultClient = new AzureKeyVaultClient(azureOptions, provider.GetRequiredService<ILogger<AzureKeyVaultClient>>());
        
        // Create and return key provider
        return new AzureKeyVaultProvider(vaultClient, azureOptions, provider.GetRequiredService<ILogger<AzureKeyVaultProvider>>());
    }
}