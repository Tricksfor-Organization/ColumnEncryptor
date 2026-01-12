using ColumnEncryptor;
using ColumnEncryptor.Common;
using ColumnEncryptor.Interfaces;
using ColumnEncryptor.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Shouldly;
using System.Security.Cryptography;

namespace ColumnEncryptor.Tests;

[TestFixture]
public class ManualKeyProviderTests
{
    private const string TestKey1Base64 = "YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXoxMjM0NTY="; // 32 bytes
    private const string TestKey2Base64 = "MTIzNDU2Nzg5MGFiY2RlZmdoaWprbG1ub3BxcnN0dXY="; // 32 bytes

    [Test]
    public void Constructor_ShouldLoadKeysFromConfiguration()
    {
        // Arrange
        var options = Options.Create(new ManualKeyProviderOptions
        {
            PrimaryKeyId = "key-1",
            Keys = new List<ManualEncryptionKey>
            {
                new ManualEncryptionKey { Id = "key-1", KeyBase64 = TestKey1Base64 }
            }
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ManualKeyProvider>();

        // Act
        var provider = new ManualKeyProvider(options, logger);

        // Assert
        var allKeys = provider.GetAllKeys().ToList();
        allKeys.ShouldNotBeEmpty();
        allKeys.Count.ShouldBe(1);
        allKeys[0].Id.ShouldBe("key-1");
    }

    [Test]
    public void GetPrimaryKey_ShouldReturnConfiguredPrimaryKey()
    {
        // Arrange
        var options = Options.Create(new ManualKeyProviderOptions
        {
            PrimaryKeyId = "key-1",
            Keys = new List<ManualEncryptionKey>
            {
                new ManualEncryptionKey { Id = "key-1", KeyBase64 = TestKey1Base64 },
                new ManualEncryptionKey { Id = "key-2", KeyBase64 = TestKey2Base64 }
            }
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ManualKeyProvider>();
        var provider = new ManualKeyProvider(options, logger);

        // Act
        var primaryKey = provider.GetPrimaryKey();

        // Assert
        primaryKey.ShouldNotBeNull();
        primaryKey.Id.ShouldBe("key-1");
    }

    [Test]
    public void GetPrimaryKey_WhenNoPrimaryKeySpecified_ShouldUseFirstKey()
    {
        // Arrange
        var options = Options.Create(new ManualKeyProviderOptions
        {
            PrimaryKeyId = "", // No primary key specified
            Keys = new List<ManualEncryptionKey>
            {
                new ManualEncryptionKey { Id = "key-1", KeyBase64 = TestKey1Base64 },
                new ManualEncryptionKey { Id = "key-2", KeyBase64 = TestKey2Base64 }
            }
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ManualKeyProvider>();
        var provider = new ManualKeyProvider(options, logger);

        // Act
        var primaryKey = provider.GetPrimaryKey();

        // Assert
        primaryKey.ShouldNotBeNull();
        primaryKey.Id.ShouldBe("key-1"); // Should use first key
    }

    [Test]
    public void GetKey_ShouldReturnSpecificKey()
    {
        // Arrange
        var options = Options.Create(new ManualKeyProviderOptions
        {
            PrimaryKeyId = "key-1",
            Keys = new List<ManualEncryptionKey>
            {
                new ManualEncryptionKey { Id = "key-1", KeyBase64 = TestKey1Base64 },
                new ManualEncryptionKey { Id = "key-2", KeyBase64 = TestKey2Base64 }
            }
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ManualKeyProvider>();
        var provider = new ManualKeyProvider(options, logger);

        // Act
        var key = provider.GetKey("key-2");

        // Assert
        key.ShouldNotBeNull();
        key!.Id.ShouldBe("key-2");
    }

    [Test]
    public void GetKey_WithNonExistentKeyId_ShouldReturnNull()
    {
        // Arrange
        var options = Options.Create(new ManualKeyProviderOptions
        {
            PrimaryKeyId = "key-1",
            Keys = new List<ManualEncryptionKey>
            {
                new ManualEncryptionKey { Id = "key-1", KeyBase64 = TestKey1Base64 }
            }
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ManualKeyProvider>();
        var provider = new ManualKeyProvider(options, logger);

        // Act
        var key = provider.GetKey("non-existent");

        // Assert
        key.ShouldBeNull();
    }

    [Test]
    public void GetAllKeys_ShouldReturnAllConfiguredKeys()
    {
        // Arrange
        var options = Options.Create(new ManualKeyProviderOptions
        {
            PrimaryKeyId = "key-1",
            Keys = new List<ManualEncryptionKey>
            {
                new ManualEncryptionKey { Id = "key-1", KeyBase64 = TestKey1Base64 },
                new ManualEncryptionKey { Id = "key-2", KeyBase64 = TestKey2Base64 }
            }
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ManualKeyProvider>();
        var provider = new ManualKeyProvider(options, logger);

        // Act
        var keys = provider.GetAllKeys().ToList();

        // Assert
        keys.Count.ShouldBe(2);
        keys.Any(k => k.Id == "key-1").ShouldBeTrue();
        keys.Any(k => k.Id == "key-2").ShouldBeTrue();
    }

    [Test]
    public void AddKey_ShouldAddKeyToProvider()
    {
        // Arrange
        var options = Options.Create(new ManualKeyProviderOptions
        {
            PrimaryKeyId = "key-1",
            Keys = new List<ManualEncryptionKey>
            {
                new ManualEncryptionKey { Id = "key-1", KeyBase64 = TestKey1Base64 }
            }
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ManualKeyProvider>();
        var provider = new ManualKeyProvider(options, logger);

        var newKeyBytes = RandomNumberGenerator.GetBytes(32);
        var newKey = new EncryptionKey("key-3", newKeyBytes, DateTime.UtcNow);

        // Act
        provider.AddKey(newKey);

        // Assert
        var retrievedKey = provider.GetKey("key-3");
        retrievedKey.ShouldNotBeNull();
        retrievedKey!.Id.ShouldBe("key-3");
        retrievedKey.KeyBytes.ShouldBe(newKeyBytes);
    }

    [Test]
    public void AddKey_WhenNoPrimaryKeySet_ShouldMakeItPrimary()
    {
        // Arrange - Empty configuration
        var options = Options.Create(new ManualKeyProviderOptions
        {
            PrimaryKeyId = "",
            Keys = new List<ManualEncryptionKey>()
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ManualKeyProvider>();
        var provider = new ManualKeyProvider(options, logger);

        var newKeyBytes = RandomNumberGenerator.GetBytes(32);
        var newKey = new EncryptionKey("key-1", newKeyBytes, DateTime.UtcNow);

        // Act
        provider.AddKey(newKey);

        // Assert
        var primaryKey = provider.GetPrimaryKey();
        primaryKey.ShouldNotBeNull();
        primaryKey.Id.ShouldBe("key-1");
    }

    [Test]
    public void PromoteKey_ShouldChangeThePrimaryKey()
    {
        // Arrange
        var options = Options.Create(new ManualKeyProviderOptions
        {
            PrimaryKeyId = "key-1",
            Keys = new List<ManualEncryptionKey>
            {
                new ManualEncryptionKey { Id = "key-1", KeyBase64 = TestKey1Base64 },
                new ManualEncryptionKey { Id = "key-2", KeyBase64 = TestKey2Base64 }
            }
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ManualKeyProvider>();
        var provider = new ManualKeyProvider(options, logger);

        // Act
        provider.PromoteKey("key-2");

        // Assert
        var primaryKey = provider.GetPrimaryKey();
        primaryKey.Id.ShouldBe("key-2");
    }

    [Test]
    public void PromoteKey_WithNonExistentKey_ShouldThrowException()
    {
        // Arrange
        var options = Options.Create(new ManualKeyProviderOptions
        {
            PrimaryKeyId = "key-1",
            Keys = new List<ManualEncryptionKey>
            {
                new ManualEncryptionKey { Id = "key-1", KeyBase64 = TestKey1Base64 }
            }
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ManualKeyProvider>();
        var provider = new ManualKeyProvider(options, logger);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => provider.PromoteKey("non-existent"));
    }

    [Test]
    public void Constructor_WithInvalidKeyLength_ShouldThrowException()
    {
        // Arrange - Use a key that's not 32 bytes
        var invalidKeyBase64 = "c2hvcnRrZXk="; // Only 8 bytes when decoded
        var options = Options.Create(new ManualKeyProviderOptions
        {
            PrimaryKeyId = "key-1",
            Keys = new List<ManualEncryptionKey>
            {
                new ManualEncryptionKey { Id = "key-1", KeyBase64 = invalidKeyBase64 }
            }
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ManualKeyProvider>();

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => new ManualKeyProvider(options, logger));
    }

    [Test]
    public void Constructor_WithInvalidBase64_ShouldThrowException()
    {
        // Arrange - Use invalid Base64 string
        var options = Options.Create(new ManualKeyProviderOptions
        {
            PrimaryKeyId = "key-1",
            Keys = new List<ManualEncryptionKey>
            {
                new ManualEncryptionKey { Id = "key-1", KeyBase64 = "not-valid-base64!!!" }
            }
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ManualKeyProvider>();

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => new ManualKeyProvider(options, logger));
    }

    [Test]
    public void Constructor_WithNonExistentPrimaryKey_ShouldThrowException()
    {
        // Arrange
        var options = Options.Create(new ManualKeyProviderOptions
        {
            PrimaryKeyId = "non-existent",
            Keys = new List<ManualEncryptionKey>
            {
                new ManualEncryptionKey { Id = "key-1", KeyBase64 = TestKey1Base64 }
            }
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ManualKeyProvider>();

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => new ManualKeyProvider(options, logger));
    }

    [Test]
    public void AddColumnEncryption_WithManualProvider_ShouldConfigureCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddColumnEncryption(options =>
        {
            options.KeyProvider = KeyProviderType.Manual;
            options.Manual = new ManualKeyProviderOptions
            {
                PrimaryKeyId = "test-key",
                Keys = new List<ManualEncryptionKey>
                {
                    new ManualEncryptionKey { Id = "test-key", KeyBase64 = TestKey1Base64 }
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var encryptionOptions = serviceProvider.GetRequiredService<EncryptionOptions>();
        encryptionOptions.ShouldNotBeNull();
        encryptionOptions.KeyProvider.ShouldBe(KeyProviderType.Manual);
        encryptionOptions.Manual.ShouldNotBeNull();
        encryptionOptions.Manual!.PrimaryKeyId.ShouldBe("test-key");

        var keyProvider = serviceProvider.GetRequiredService<IKeyProvider>();
        keyProvider.ShouldNotBeNull();
        keyProvider.ShouldBeOfType<ManualKeyProvider>();

        var encryptionService = serviceProvider.GetRequiredService<IEncryptionService>();
        encryptionService.ShouldNotBeNull();
    }

    [Test]
    public void AddColumnEncryption_WithServiceProvider_Manual_ShouldConfigureCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var manualConfig = new ManualConfiguration
        {
            PrimaryKeyId = "key-1",
            Keys = new List<ManualEncryptionKey>
            {
                new ManualEncryptionKey { Id = "key-1", KeyBase64 = TestKey1Base64 }
            }
        };
        services.AddSingleton(manualConfig);

        // Act
        services.AddColumnEncryption((options, sp) =>
        {
            var config = sp.GetRequiredService<ManualConfiguration>();
            
            options.KeyProvider = KeyProviderType.Manual;
            options.Manual = new ManualKeyProviderOptions
            {
                PrimaryKeyId = config.PrimaryKeyId,
                Keys = config.Keys
            };
        });

        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var encryptionOptions = serviceProvider.GetRequiredService<EncryptionOptions>();
        encryptionOptions.ShouldNotBeNull();
        encryptionOptions.KeyProvider.ShouldBe(KeyProviderType.Manual);
        encryptionOptions.Manual!.PrimaryKeyId.ShouldBe("key-1");
    }

    [Test]
    public void AddColumnEncryption_WithManualProvider_NullOptions_ShouldThrowException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act & Assert - Exception is thrown during AddColumnEncryption
        Should.Throw<InvalidOperationException>(() =>
        {
            services.AddColumnEncryption(options =>
            {
                options.KeyProvider = KeyProviderType.Manual;
                // Intentionally not setting Manual options
                options.Manual = null;
            });
        });
    }

    [Test]
    public void ManualKeyProvider_WithCreatedUtc_ShouldPreserveTimestamp()
    {
        // Arrange
        var createdDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var options = Options.Create(new ManualKeyProviderOptions
        {
            PrimaryKeyId = "key-1",
            Keys = new List<ManualEncryptionKey>
            {
                new ManualEncryptionKey 
                { 
                    Id = "key-1", 
                    KeyBase64 = TestKey1Base64,
                    CreatedUtc = createdDate
                }
            }
        });

        var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ManualKeyProvider>();
        var provider = new ManualKeyProvider(options, logger);

        // Act
        var key = provider.GetKey("key-1");

        // Assert
        key.ShouldNotBeNull();
        key!.CreatedUtc.ShouldBe(createdDate);
    }
}

// Test helper class
public class ManualConfiguration
{
    public string PrimaryKeyId { get; set; } = string.Empty;
    public List<ManualEncryptionKey> Keys { get; set; } = new();
}
