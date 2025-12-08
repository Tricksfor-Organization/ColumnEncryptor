using ColumnEncryptor;
using ColumnEncryptor.Interfaces;
using Microsoft.Extensions.DependencyInjection;
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
