using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using ColumnEncryptor.Common;
using ColumnEncryptor.Interfaces;
using FluentAssertions;
using NUnit.Framework;

namespace ColumnEncryptor.Tests;

[TestFixture]
public class HashiCorpTests
{
    private IServiceScope? _scope;
    private IContainer? _vaultContainer;
    private const string VaultImage = "hashicorp/vault:latest";
    private const int VaultPort = 8200;
    private string _vaultUrl = string.Empty;
    private const string VaultToken = "test-root-token";

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        // Start HashiCorp Vault container
        _vaultContainer = new ContainerBuilder()
            .WithImage(VaultImage)
            .WithCleanUp(true)
            .WithName($"vault-test-{Guid.NewGuid():N}")
            // Bind the container's Vault port to a random free host port to avoid collisions
            .WithPortBinding(VaultPort, true)
            // Start Vault in dev mode explicitly; env vars alone won't start the server
            .WithCommand(
                "server",
                "-dev",
                $"-dev-root-token-id={VaultToken}",
                "-dev-listen-address=0.0.0.0:8200")
            // Don't block on container logs/ports here; we'll poll the health endpoint below
            .Build();

        await _vaultContainer.StartAsync();

        // Get the mapped port for Vault
        var mappedPort = _vaultContainer.GetMappedPublicPort(VaultPort);
        _vaultUrl = $"http://localhost:{mappedPort}";

        // Wait for Vault to be ready by checking health endpoint
        await WaitForVaultHealthy(_vaultUrl);

        // Setup DI container
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        
        services.AddColumnEncryptor(new EncryptionOptions
        {
            KeyProvider = KeyProviderType.HashiCorpVault,
            Vault = new VaultOptions
            {
                ServerUrl = _vaultUrl,
                AuthMethod = VaultAuthMethod.Token,
                Token = VaultToken,
                KeysPath = "secret/column-encryption/keys",
                CacheExpiryMinutes = 1
            }
        });

        var provider = services.BuildServiceProvider();
        _scope = provider.CreateScope();

        // Wait a bit for Vault to be fully ready
        await Task.Delay(2000);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        _scope?.Dispose();
        
        if (_vaultContainer is not null)
        {
            await _vaultContainer.StopAsync();
            await _vaultContainer.DisposeAsync();
        }
    }

    private static async Task WaitForVaultHealthy(string vaultUrl)
    {
        using var client = new HttpClient();
        var healthUrl = $"{vaultUrl}/v1/sys/health";
        var maxAttempts = 30;
        var delayMs = 1000;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await client.GetAsync(healthUrl);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Vault is healthy after {attempt} attempts");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Attempt {attempt}/{maxAttempts} - Vault not ready: {ex.Message}");
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(delayMs);
            }
        }

        throw new TimeoutException($"Vault did not become healthy within {maxAttempts} attempts");
    }

    [Test]
    public void Should_Initialize_KeyProvider_Successfully()
    {
        // Arrange & Act
        var keyProvider = _scope?.ServiceProvider.GetRequiredService<IKeyProvider>();
        
        // Assert
        keyProvider.Should().NotBeNull();
        keyProvider.Should().BeOfType<ColumnEncryptor.Providers.VaultKeyProvider>();
    }

    [Test]
    public void Should_Initialize_EncryptionService_Successfully()
    {
        // Arrange & Act
        var encryptionService = _scope?.ServiceProvider.GetRequiredService<IEncryptionService>();
        
        // Assert
        encryptionService.Should().NotBeNull();
        encryptionService.Should().BeOfType<ColumnEncryptor.Services.AesGcmEncryptionService>();
    }

    [Test]
    public async Task Should_Add_And_Retrieve_Encryption_Key()
    {
        // Arrange
        var keyProvider = _scope?.ServiceProvider.GetRequiredService<IKeyProvider>();
        keyProvider.Should().NotBeNull();

        var testKeyId = $"test-key-{Guid.NewGuid():N}";
        var keyBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var testKey = new EncryptionKey(testKeyId, keyBytes, DateTime.UtcNow);

        // Act
        keyProvider!.AddKey(testKey);
        
        // Allow some time for Vault operations
        await Task.Delay(1000);
        
        var retrievedKey = keyProvider.GetKey(testKeyId);

        // Assert
        retrievedKey.Should().NotBeNull();
        retrievedKey!.Id.Should().Be(testKeyId);
        retrievedKey.KeyBytes.Should().BeEquivalentTo(keyBytes);
    }

    [Test]
    public async Task Should_Set_And_Get_Primary_Key()
    {
        // Arrange
        var keyProvider = _scope?.ServiceProvider.GetRequiredService<IKeyProvider>();
        keyProvider.Should().NotBeNull();

        var primaryKeyId = $"primary-key-{Guid.NewGuid():N}";
        var keyBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var primaryKey = new EncryptionKey(primaryKeyId, keyBytes, DateTime.UtcNow);

        // Act
        keyProvider!.AddKey(primaryKey);
        
        // Allow some time for Vault operations
        await Task.Delay(1000);
        
        keyProvider.PromoteKey(primaryKeyId);
        
        // Allow some time for Vault operations
        await Task.Delay(1000);
        
        var retrievedPrimaryKey = keyProvider.GetPrimaryKey();

        // Assert
        retrievedPrimaryKey.Should().NotBeNull();
        retrievedPrimaryKey.Id.Should().Be(primaryKeyId);
        retrievedPrimaryKey.KeyBytes.Should().BeEquivalentTo(keyBytes);
    }

    [Test]
    public async Task Should_Encrypt_And_Decrypt_Data()
    {
        // Arrange
        var encryptionService = _scope?.ServiceProvider.GetRequiredService<IEncryptionService>();
        var keyProvider = _scope?.ServiceProvider.GetRequiredService<IKeyProvider>();
        
        encryptionService.Should().NotBeNull();
        keyProvider.Should().NotBeNull();

        // Ensure we have a primary key
        var testKeyId = $"encryption-test-key-{Guid.NewGuid():N}";
        var keyBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var testKey = new EncryptionKey(testKeyId, keyBytes, DateTime.UtcNow);
        
        keyProvider!.AddKey(testKey);
        await Task.Delay(1000);
        keyProvider.PromoteKey(testKeyId);
        await Task.Delay(1000);

        const string originalText = "This is a secret message that should be encrypted";

        // Act
        var encryptedText = encryptionService!.Encrypt(originalText);
        var decryptedText = encryptionService.Decrypt(encryptedText);

        // Assert
        encryptedText.Should().NotBeNullOrEmpty();
        encryptedText.Should().NotBe(originalText);
        encryptedText.Should().StartWith("{"); // Should be JSON
        
        decryptedText.Should().Be(originalText);
    }

    [Test]
    public async Task Should_Handle_Multiple_Keys_And_Rotation()
    {
        // Arrange
        var keyProvider = _scope?.ServiceProvider.GetRequiredService<IKeyProvider>();
        keyProvider.Should().NotBeNull();

        var key1Id = $"key1-{Guid.NewGuid():N}";
        var key2Id = $"key2-{Guid.NewGuid():N}";
        var key1Bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var key2Bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        
        var key1 = new EncryptionKey(key1Id, key1Bytes, DateTime.UtcNow);
        var key2 = new EncryptionKey(key2Id, key2Bytes, DateTime.UtcNow.AddMinutes(1));

        // Act - Add both keys
        keyProvider!.AddKey(key1);
        await Task.Delay(500);
        keyProvider.AddKey(key2);
        await Task.Delay(500);

        // Set key1 as primary
        keyProvider.PromoteKey(key1Id);
        await Task.Delay(500);

        var primaryKey1 = keyProvider.GetPrimaryKey();
        
        // Rotate to key2
        keyProvider.PromoteKey(key2Id);
        await Task.Delay(500);
        
        var primaryKey2 = keyProvider.GetPrimaryKey();
        
        var allKeys = keyProvider.GetAllKeys();
        var testKeys = allKeys.Where(k => k.Id == key1Id || k.Id == key2Id).ToList();

        // Assert
        primaryKey1.Id.Should().Be(key1Id);
        primaryKey2.Id.Should().Be(key2Id);
        
        testKeys.Should().HaveCount(2);
        testKeys.Should().Contain(k => k.Id == key1Id);
        testKeys.Should().Contain(k => k.Id == key2Id);
    }

    [Test]
    public async Task Should_Decrypt_Data_Encrypted_With_Previous_Key()
    {
        // Arrange
        var encryptionService = _scope?.ServiceProvider.GetRequiredService<IEncryptionService>();
        var keyProvider = _scope?.ServiceProvider.GetRequiredService<IKeyProvider>();
        
        encryptionService.Should().NotBeNull();
        keyProvider.Should().NotBeNull();

        // Create two keys
        var oldKeyId = $"old-key-{Guid.NewGuid():N}";
        var newKeyId = $"new-key-{Guid.NewGuid():N}";
        var oldKeyBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var newKeyBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        
        var oldKey = new EncryptionKey(oldKeyId, oldKeyBytes, DateTime.UtcNow);
        var newKey = new EncryptionKey(newKeyId, newKeyBytes, DateTime.UtcNow.AddMinutes(1));

        // Add old key and encrypt data
        keyProvider!.AddKey(oldKey);
        await Task.Delay(500);
        keyProvider.PromoteKey(oldKeyId);
        await Task.Delay(500);

        const string originalText = "Data encrypted with old key";
        var encryptedWithOldKey = encryptionService!.Encrypt(originalText);

        // Add new key and make it primary
        keyProvider.AddKey(newKey);
        await Task.Delay(500);
        keyProvider.PromoteKey(newKeyId);
        await Task.Delay(500);

        // Act - Try to decrypt data encrypted with old key
        var decryptedText = encryptionService.Decrypt(encryptedWithOldKey);

        // Assert
        decryptedText.Should().Be(originalText);
    }
}
