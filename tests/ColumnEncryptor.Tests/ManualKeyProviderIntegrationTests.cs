using ColumnEncryptor;
using ColumnEncryptor.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Shouldly;
using System.Security.Cryptography;

namespace ColumnEncryptor.Tests;

[TestFixture]
public class ManualKeyProviderIntegrationTests
{
    private const string TestKey1Base64 = "YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXoxMjM0NTY="; // 32 bytes

    [Test]
    public void Should_Initialize_Keys_For_Empty_ManualKeyProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));

        // Start with no keys
        services.AddColumnEncryption(options =>
        {
            options.KeyProvider = KeyProviderType.Manual;
            options.Manual = new ManualKeyProviderOptions
            {
                Keys = new List<ManualEncryptionKey>()
            };
        });

        var serviceProvider = services.BuildServiceProvider();

        // Act - Initialize keys
        serviceProvider.InitializeEncryptionKeysAsync().Wait();

        // Assert - Should have one key now
        var keyProvider = serviceProvider.GetRequiredService<IKeyProvider>();
        var keys = keyProvider.GetAllKeys().ToList();
        keys.Count.ShouldBe(1);

        var primaryKey = keyProvider.GetPrimaryKey();
        primaryKey.ShouldNotBeNull();
    }

    [Test]
    public void Should_Support_Multiple_Keys_With_ManualKeyProvider()
    {
        // Arrange - Setup with two keys
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));

        var key1Base64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var key2Base64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        services.AddColumnEncryption(options =>
        {
            options.KeyProvider = KeyProviderType.Manual;
            options.Manual = new ManualKeyProviderOptions
            {
                PrimaryKeyId = "key-1",
                Keys = new List<ManualEncryptionKey>
                {
                    new ManualEncryptionKey { Id = "key-1", KeyBase64 = key1Base64 },
                    new ManualEncryptionKey { Id = "key-2", KeyBase64 = key2Base64 }
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var keyProvider = serviceProvider.GetRequiredService<IKeyProvider>();
        
        // Assert - Should have both keys
        var allKeys = keyProvider.GetAllKeys().ToList();
        allKeys.Count.ShouldBe(2);

        var primaryKey = keyProvider.GetPrimaryKey();
        primaryKey.Id.ShouldBe("key-1");

        // Promote key-2
        keyProvider.PromoteKey("key-2");
        
        var newPrimaryKey = keyProvider.GetPrimaryKey();
        newPrimaryKey.Id.ShouldBe("key-2");
    }

    [Test]
    public void Should_Encrypt_And_Decrypt_With_ManualKeyProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));

        services.AddColumnEncryption(options =>
        {
            options.KeyProvider = KeyProviderType.Manual;
            options.Manual = new ManualKeyProviderOptions
            {
                PrimaryKeyId = "test-key",
                Keys = new List<ManualEncryptionKey>
                {
                    new ManualEncryptionKey 
                    { 
                        Id = "test-key", 
                        KeyBase64 = TestKey1Base64 
                    }
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var encryptionService = serviceProvider.GetRequiredService<IEncryptionService>();

        // Act
        var plainText = "sensitive-data@example.com";
        var encrypted = encryptionService.Encrypt(plainText);
        var decrypted = encryptionService.Decrypt(encrypted);

        // Assert
        encrypted.ShouldNotBe(plainText);
        decrypted.ShouldBe(plainText);
    }
}
