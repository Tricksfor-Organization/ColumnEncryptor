using ColumnEncryptor;
using ColumnEncryptor.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace ColumnEncryptor.Tests;

[TestFixture]
public class ServiceProviderConfigurationTests
{
    [Test]
    public void AddColumnEncryption_WithServiceProvider_ShouldResolveOptionsFromServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Add test configuration
        var testOptions = new TestApplicationOptions
        {
            VaultUrl = "http://test-vault:8200",
            VaultToken = "test-token"
        };
        services.AddSingleton(Options.Create(testOptions));
        
        // Act
        services.AddColumnEncryption((options, sp) =>
        {
            var appOptions = sp.GetRequiredService<IOptions<TestApplicationOptions>>().Value;
            
            options.KeyProvider = KeyProviderType.HashiCorpVault;
            options.Vault = new VaultOptions
            {
                ServerUrl = appOptions.VaultUrl,
                AuthMethod = VaultAuthMethod.Token,
                Token = appOptions.VaultToken,
                KeysPath = "secret/test-keys"
            };
        });
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert
        var encryptionOptions = serviceProvider.GetRequiredService<EncryptionOptions>();
        Assert.That(encryptionOptions, Is.Not.Null);
        Assert.That(encryptionOptions.KeyProvider, Is.EqualTo(KeyProviderType.HashiCorpVault));
        Assert.That(encryptionOptions.Vault, Is.Not.Null);
        Assert.That(encryptionOptions.Vault!.ServerUrl, Is.EqualTo("http://test-vault:8200"));
        Assert.That(encryptionOptions.Vault.Token, Is.EqualTo("test-token"));
    }
    
    [Test]
    public void AddColumnEncryption_WithServiceProvider_ShouldResolveMultipleServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Add test configuration with a different set of options
        var vaultConfig = new VaultConfiguration
        {
            ServerUrl = "http://config-vault:8200",
            Token = "config-token",
            KeysPath = "secret/encryption-keys"
        };
        
        services.AddSingleton(vaultConfig);
        
        // Act
        services.AddColumnEncryption((options, sp) =>
        {
            var config = sp.GetRequiredService<VaultConfiguration>();
            
            options.KeyProvider = KeyProviderType.HashiCorpVault;
            options.Vault = new VaultOptions
            {
                ServerUrl = config.ServerUrl,
                AuthMethod = VaultAuthMethod.Token,
                Token = config.Token,
                KeysPath = config.KeysPath
            };
        });
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert
        var encryptionOptions = serviceProvider.GetRequiredService<EncryptionOptions>();
        Assert.That(encryptionOptions, Is.Not.Null);
        Assert.That(encryptionOptions.Vault!.ServerUrl, Is.EqualTo("http://config-vault:8200"));
        Assert.That(encryptionOptions.Vault.Token, Is.EqualTo("config-token"));
    }
    
    [Test]
    public void AddColumnEncryption_WithServiceProvider_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        var testOptions = new TestApplicationOptions
        {
            VaultUrl = "http://localhost:8200",
            VaultToken = "test-token"
        };
        services.AddSingleton(Options.Create(testOptions));
        
        // Act
        services.AddColumnEncryption((options, sp) =>
        {
            var appOptions = sp.GetRequiredService<IOptions<TestApplicationOptions>>().Value;
            
            options.KeyProvider = KeyProviderType.HashiCorpVault;
            options.Vault = new VaultOptions
            {
                ServerUrl = appOptions.VaultUrl,
                AuthMethod = VaultAuthMethod.Token,
                Token = appOptions.VaultToken,
                KeysPath = "secret/test-keys"
            };
        });
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert - Check that EncryptionOptions is registered correctly
        var encryptionOptions = serviceProvider.GetRequiredService<EncryptionOptions>();
        Assert.That(encryptionOptions, Is.Not.Null);
        Assert.That(encryptionOptions.KeyProvider, Is.EqualTo(KeyProviderType.HashiCorpVault));
        Assert.That(encryptionOptions.Vault, Is.Not.Null);
        Assert.That(encryptionOptions.Vault!.ServerUrl, Is.EqualTo("http://localhost:8200"));
        Assert.That(encryptionOptions.Vault.Token, Is.EqualTo("test-token"));
        
        // Note: We don't test IEncryptionService or IKeyProvider resolution here
        // because they require a real Vault connection
    }
    
    [Test]
    public void AddColumnEncryption_WithoutServiceProvider_ShouldStillWork()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Act - Using the old overload without IServiceProvider
        services.AddColumnEncryption(options =>
        {
            options.KeyProvider = KeyProviderType.HashiCorpVault;
            options.Vault = new VaultOptions
            {
                ServerUrl = "http://localhost:8200",
                AuthMethod = VaultAuthMethod.Token,
                Token = "test-token",
                KeysPath = "secret/test-keys"
            };
        });
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert
        var encryptionOptions = serviceProvider.GetRequiredService<EncryptionOptions>();
        Assert.That(encryptionOptions, Is.Not.Null);
        Assert.That(encryptionOptions.KeyProvider, Is.EqualTo(KeyProviderType.HashiCorpVault));
    }
    
    [Test]
    public void AddColumnEncryption_WithServiceProvider_AzureKeyVault_ShouldConfigureCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        var azureConfig = new AzureConfiguration
        {
            VaultUrl = "https://test-vault.vault.azure.net/",
            TenantId = "test-tenant-id",
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        };
        services.AddSingleton(azureConfig);
        
        // Act
        services.AddColumnEncryption((options, sp) =>
        {
            var config = sp.GetRequiredService<AzureConfiguration>();
            
            options.KeyProvider = KeyProviderType.AzureKeyVault;
            options.AzureKeyVault = new AzureKeyVaultOptions
            {
                VaultUrl = config.VaultUrl,
                AuthMethod = AzureAuthMethod.ServicePrincipal,
                TenantId = config.TenantId,
                ClientId = config.ClientId,
                ClientSecret = config.ClientSecret,
                KeyPrefix = "test-keys"
            };
        });
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert
        var encryptionOptions = serviceProvider.GetRequiredService<EncryptionOptions>();
        Assert.That(encryptionOptions, Is.Not.Null);
        Assert.That(encryptionOptions.KeyProvider, Is.EqualTo(KeyProviderType.AzureKeyVault));
        Assert.That(encryptionOptions.AzureKeyVault, Is.Not.Null);
        Assert.That(encryptionOptions.AzureKeyVault!.VaultUrl, Is.EqualTo("https://test-vault.vault.azure.net/"));
        Assert.That(encryptionOptions.AzureKeyVault.TenantId, Is.EqualTo("test-tenant-id"));
        Assert.That(encryptionOptions.AzureKeyVault.ClientId, Is.EqualTo("test-client-id"));
    }
    
    [Test]
    public void AddColumnEncryption_WithServiceProvider_NullConfiguration_ShouldThrowException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Act
        services.AddColumnEncryption((options, sp) =>
        {
            options.KeyProvider = KeyProviderType.HashiCorpVault;
            // Intentionally not setting Vault options
            options.Vault = null;
        });
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            serviceProvider.GetRequiredService<IKeyProvider>();
        });
    }
    
    [Test]
    public void AddColumnEncryption_WithServiceProvider_MissingService_ShouldThrowException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Act
        services.AddColumnEncryption((options, sp) =>
        {
            // Try to get a service that doesn't exist
            _ = sp.GetRequiredService<IMissingService>();
        });
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            serviceProvider.GetRequiredService<EncryptionOptions>();
        });
    }
    
    [Test]
    public void AddColumnEncryption_WithServiceProvider_MultipleOptionsResolution_ShouldWorkCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Add multiple configuration sources
        var vaultConfig = new VaultConfiguration
        {
            ServerUrl = "http://vault:8200",
            Token = "vault-token",
            KeysPath = "secret/keys"
        };
        
        var appSettings = new AppSettings
        {
            Environment = "Development",
            UseVault = true
        };
        
        services.AddSingleton(vaultConfig);
        services.AddSingleton(appSettings);
        
        // Act
        services.AddColumnEncryption((options, sp) =>
        {
            var config = sp.GetRequiredService<VaultConfiguration>();
            var settings = sp.GetRequiredService<AppSettings>();
            
            if (settings.UseVault)
            {
                options.KeyProvider = KeyProviderType.HashiCorpVault;
                options.Vault = new VaultOptions
                {
                    ServerUrl = config.ServerUrl,
                    AuthMethod = VaultAuthMethod.Token,
                    Token = config.Token,
                    KeysPath = config.KeysPath
                };
            }
        });
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert
        var encryptionOptions = serviceProvider.GetRequiredService<EncryptionOptions>();
        Assert.That(encryptionOptions, Is.Not.Null);
        Assert.That(encryptionOptions.KeyProvider, Is.EqualTo(KeyProviderType.HashiCorpVault));
        Assert.That(encryptionOptions.Vault!.ServerUrl, Is.EqualTo("http://vault:8200"));
    }
    
    [Test]
    public void AddColumnEncryption_WithServiceProvider_ShouldResolveILogger()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        var loggerInvoked = false;
        var testOptions = new TestApplicationOptions
        {
            VaultUrl = "http://test:8200",
            VaultToken = "test-token"
        };
        services.AddSingleton(Options.Create(testOptions));
        
        // Act
        services.AddColumnEncryption((options, sp) =>
        {
            var logger = sp.GetService<ILogger<ServiceProviderConfigurationTests>>();
            if (logger != null)
            {
                loggerInvoked = true;
            }
            
            var appOptions = sp.GetRequiredService<IOptions<TestApplicationOptions>>().Value;
            
            options.KeyProvider = KeyProviderType.HashiCorpVault;
            options.Vault = new VaultOptions
            {
                ServerUrl = appOptions.VaultUrl,
                AuthMethod = VaultAuthMethod.Token,
                Token = appOptions.VaultToken,
                KeysPath = "secret/test-keys"
            };
        });
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert
        var encryptionOptions = serviceProvider.GetRequiredService<EncryptionOptions>();
        Assert.That(encryptionOptions, Is.Not.Null);
        Assert.That(loggerInvoked, Is.True, "Logger should be resolvable from service provider");
    }
    
    [Test]
    public void AddColumnEncryptor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
        {
            services.AddColumnEncryptor(null!);
        });
    }
    
    [Test]
    public void AddColumnEncryption_WithoutServiceProvider_UnsupportedKeyProvider_ShouldThrowNotSupportedException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Act & Assert - Exception is thrown during AddColumnEncryption, not during service resolution
        Assert.Throws<NotSupportedException>(() =>
        {
            services.AddColumnEncryption(options =>
            {
                options.KeyProvider = (KeyProviderType)999; // Invalid key provider
            });
        });
    }
    
    [Test]
    public void AddColumnEncryption_WithServiceProvider_UnsupportedKeyProvider_ShouldThrowNotSupportedException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Act
        services.AddColumnEncryption((options, sp) =>
        {
            options.KeyProvider = (KeyProviderType)999; // Invalid key provider
        });
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert
        Assert.Throws<NotSupportedException>(() =>
        {
            serviceProvider.GetRequiredService<IKeyProvider>();
        });
    }
    
    [Test]
    public void AddColumnEncryption_WithServiceProvider_AzureKeyVault_NullOptions_ShouldThrowException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // Act
        services.AddColumnEncryption((options, sp) =>
        {
            options.KeyProvider = KeyProviderType.AzureKeyVault;
            // Intentionally not setting AzureKeyVault options
            options.AzureKeyVault = null;
        });
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            serviceProvider.GetRequiredService<IKeyProvider>();
        });
    }
    
    [Test]
    public void AddColumnEncryption_BothOverloads_ShouldBeAvailable()
    {
        // Arrange
        var services1 = new ServiceCollection();
        var services2 = new ServiceCollection();
        services1.AddLogging();
        services2.AddLogging();
        
        // Act - Test both overloads can be called
        services1.AddColumnEncryption(options =>
        {
            options.KeyProvider = KeyProviderType.HashiCorpVault;
            options.Vault = new VaultOptions
            {
                ServerUrl = "http://localhost:8200",
                AuthMethod = VaultAuthMethod.Token,
                Token = "test-token",
                KeysPath = "secret/keys"
            };
        });
        
        services2.AddColumnEncryption((options, sp) =>
        {
            options.KeyProvider = KeyProviderType.HashiCorpVault;
            options.Vault = new VaultOptions
            {
                ServerUrl = "http://localhost:8200",
                AuthMethod = VaultAuthMethod.Token,
                Token = "test-token",
                KeysPath = "secret/keys"
            };
        });
        
        var sp1 = services1.BuildServiceProvider();
        var sp2 = services2.BuildServiceProvider();
        
        // Assert
        var options1 = sp1.GetRequiredService<EncryptionOptions>();
        var options2 = sp2.GetRequiredService<EncryptionOptions>();
        
        Assert.That(options1, Is.Not.Null);
        Assert.That(options2, Is.Not.Null);
        Assert.That(options1.KeyProvider, Is.EqualTo(options2.KeyProvider));
    }
}

// Test helper classes
public class TestApplicationOptions
{
    public string VaultUrl { get; set; } = string.Empty;
    public string VaultToken { get; set; } = string.Empty;
}

public class VaultConfiguration
{
    public string ServerUrl { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string KeysPath { get; set; } = string.Empty;
}

public class AzureConfiguration
{
    public string VaultUrl { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public class AppSettings
{
    public string Environment { get; set; } = string.Empty;
    public bool UseVault { get; set; }
}

public interface IMissingService { }
