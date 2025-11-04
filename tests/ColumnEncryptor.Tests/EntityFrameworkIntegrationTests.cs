using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using DotNet.Testcontainers.Builders;
using ColumnEncryptor.Common;
using ColumnEncryptor.Interfaces;
using ColumnEncryptor.Attributes;
using ColumnEncryptor.Extensions;
using FluentAssertions;
using NUnit.Framework;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DotNet.Testcontainers.Containers;
using Testcontainers.MsSql;

namespace ColumnEncryptor.Tests;

[TestFixture]
public class EntityFrameworkIntegrationTests
{
    private IServiceScope? _scope;
    private MsSqlContainer? _sqlContainer;
    private IContainer? _vaultContainer;
    private string _connectionString = string.Empty;
    private string _vaultUrl = string.Empty;
    private const string SqlServerPassword = "Test123!@#";
    private const string VaultImage = "hashicorp/vault:latest";
    private const int VaultPort = 8200;
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

        // Start SQL Server container
        _sqlContainer = new MsSqlBuilder()
            .WithPassword(SqlServerPassword)
            .WithCleanUp(true)
            .WithName($"sql-test-{Guid.NewGuid():N}")
            .Build();

        await _sqlContainer.StartAsync();

        // Get connection string
        _connectionString = _sqlContainer.GetConnectionString();

        // Setup DI container with HashiCorp Vault
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        
        // Use real SQL Server container
        services.AddDbContext<TestDbContext>(options => 
            options.UseSqlServer(_connectionString));

        // Add column encryption with HashiCorp Vault
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

        // Initialize encryption keys in Vault
        var keyProvider = _scope.ServiceProvider.GetRequiredService<IKeyProvider>();
        var primaryKeyId = "ef-test-primary-key";
        var keyBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var primaryKey = new EncryptionKey(primaryKeyId, keyBytes, DateTime.UtcNow);
        
        // Add the key to Vault
        keyProvider.AddKey(primaryKey);
        
        // Allow some time for Vault operations
        await Task.Delay(1000);
        
        // Promote it to primary key
        keyProvider.PromoteKey(primaryKeyId);
        
        // Allow some time for Vault operations
        await Task.Delay(1000);

        // Initialize database
        var dbContext = _scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_scope != null)
        {
            var dbContext = _scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await dbContext.Database.EnsureDeletedAsync();
            _scope.Dispose();
        }
        
        if (_vaultContainer is not null)
        {
            await _vaultContainer.StopAsync();
            await _vaultContainer.DisposeAsync();
        }
        
        if (_sqlContainer is not null)
        {
            await _sqlContainer.StopAsync();
            await _sqlContainer.DisposeAsync();
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
    public async Task Should_Encrypt_And_Store_User_Data()
    {
        // Arrange
        var dbContext = _scope?.ServiceProvider.GetRequiredService<TestDbContext>();
        dbContext.Should().NotBeNull();

        var user = new User
        {
            Username = "john.doe",
            Email = "john.doe@example.com",
            CreditCardNumber = "1234-5678-9012-3456",
            SocialSecurityNumber = "123-45-6789",
            DateOfBirth = new DateTime(1990, 5, 15, 0, 0, 0, DateTimeKind.Utc),
            Salary = 75000.50m,
            IsActive = true,
            Notes = "This is a confidential note about the user."
        };

        // Act
        dbContext!.Users.Add(user);
        await dbContext.SaveChangesAsync();

        // Assert - Verify data was saved
        var savedUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Username == "john.doe");
        dbContext.DecryptLoadedEntities(); // Decrypt the loaded entities
        savedUser.Should().NotBeNull();
        savedUser!.Email.Should().Be("john.doe@example.com");
        savedUser.CreditCardNumber.Should().Be("1234-5678-9012-3456");
        savedUser.SocialSecurityNumber.Should().Be("123-45-6789");
        savedUser.Salary.Should().Be(75000.50m);
        savedUser.Notes.Should().Be("This is a confidential note about the user.");

        // Verify the data is actually encrypted in the database by checking raw values
        var rawData = await GetRawDatabaseValues(dbContext, savedUser.Id);
        rawData.Should().NotBeNull();
        
        // Encrypted fields should not match plaintext values
        rawData!.CreditCardNumber.Should().NotBe("1234-5678-9012-3456");
        rawData.SocialSecurityNumber.Should().NotBe("123-45-6789");
        rawData.Notes.Should().NotBe("This is a confidential note about the user.");
        
        // Non-encrypted fields should match plaintext values
        rawData.Username.Should().Be("john.doe");
        rawData.Email.Should().Be("john.doe@example.com");
    }

    [Test]
    public async Task Should_Handle_Multiple_Users_With_Different_Encrypted_Data()
    {
        // Arrange
        var dbContext = _scope?.ServiceProvider.GetRequiredService<TestDbContext>();
        dbContext.Should().NotBeNull();

        var users = new[]
        {
            new User
            {
                Username = "alice",
                Email = "alice@example.com",
                CreditCardNumber = "4111-1111-1111-1111",
                SocialSecurityNumber = "111-22-3333",
                Salary = 85000.00m,
                Notes = "Alice's confidential data"
            },
            new User
            {
                Username = "bob",
                Email = "bob@example.com", 
                CreditCardNumber = "5555-5555-5555-4444",
                SocialSecurityNumber = "999-88-7777",
                Salary = 92000.75m,
                Notes = "Bob's private information"
            }
        };

        // Act
        dbContext!.Users.AddRange(users);
        await dbContext.SaveChangesAsync();

        // Assert
        var allUsers = await dbContext.Users
            .Where(u => u.Username == "alice" || u.Username == "bob")
            .ToListAsync();
        dbContext.DecryptLoadedEntities(); // Decrypt the loaded entities

        allUsers.Should().HaveCount(2);

        var alice = allUsers.First(u => u.Username == "alice");
        alice.CreditCardNumber.Should().Be("4111-1111-1111-1111");
        alice.SocialSecurityNumber.Should().Be("111-22-3333");
        alice.Notes.Should().Be("Alice's confidential data");

        var bob = allUsers.First(u => u.Username == "bob");
        bob.CreditCardNumber.Should().Be("5555-5555-5555-4444");
        bob.SocialSecurityNumber.Should().Be("999-88-7777");
        bob.Notes.Should().Be("Bob's private information");
    }

    [Test]
    public async Task Should_Update_Encrypted_Fields_Correctly()
    {
        // Arrange
        var dbContext = _scope?.ServiceProvider.GetRequiredService<TestDbContext>();
        dbContext.Should().NotBeNull();

        var user = new User
        {
            Username = "updatetest",
            Email = "update@example.com",
            CreditCardNumber = "1111-2222-3333-4444",
            SocialSecurityNumber = "555-66-7777",
            Salary = 50000.00m,
            Notes = "Original note"
        };

        dbContext!.Users.Add(user);
        await dbContext.SaveChangesAsync();

        // Act - Update encrypted fields
        user.CreditCardNumber = "9999-8888-7777-6666";
        user.SocialSecurityNumber = "111-11-1111";
        user.Notes = "Updated confidential note";
        user.Salary = 60000.00m;

        await dbContext.SaveChangesAsync();

        // Assert
        var updatedUser = await dbContext.Users.FirstAsync(u => u.Username == "updatetest");
        dbContext.DecryptLoadedEntities(); // Decrypt the loaded entities
        updatedUser.CreditCardNumber.Should().Be("9999-8888-7777-6666");
        updatedUser.SocialSecurityNumber.Should().Be("111-11-1111");
        updatedUser.Notes.Should().Be("Updated confidential note");
        updatedUser.Salary.Should().Be(60000.00m);
    }

    [Test]
    public async Task Should_Handle_Null_Encrypted_Values()
    {
        // Arrange
        var dbContext = _scope?.ServiceProvider.GetRequiredService<TestDbContext>();
        dbContext.Should().NotBeNull();

        var user = new User
        {
            Username = "nulltest",
            Email = "null@example.com",
            CreditCardNumber = null, // Nullable encrypted field
            SocialSecurityNumber = "123-45-6789", // Required encrypted field
            Salary = 45000.00m,
            Notes = null // Nullable encrypted field
        };

        // Act
        dbContext!.Users.Add(user);
        await dbContext.SaveChangesAsync();

        // Assert
        var savedUser = await dbContext.Users.FirstAsync(u => u.Username == "nulltest");
        dbContext.DecryptLoadedEntities(); // Decrypt the loaded entities
        savedUser.CreditCardNumber.Should().BeNull();
        savedUser.SocialSecurityNumber.Should().Be("123-45-6789");
        savedUser.Notes.Should().BeNull();
        savedUser.Salary.Should().Be(45000.00m);
    }

    [Test]
    public async Task Should_Work_With_Different_Data_Types()
    {
        // Arrange
        var dbContext = _scope?.ServiceProvider.GetRequiredService<TestDbContext>();
        dbContext.Should().NotBeNull();

        var product = new Product
        {
            Name = "Test Product",
            Description = "Public product description",
            InternalNotes = "Confidential internal notes about pricing strategy",
            Price = 199.99m,
            Cost = 89.50m, // Encrypted cost information
            SupplierInfo = "Confidential supplier details and contracts",
            IsActive = true,
            CreatedDate = DateTime.UtcNow
        };

        // Act
        dbContext!.Products.Add(product);
        await dbContext.SaveChangesAsync();

        // Assert
        var savedProduct = await dbContext.Products.FirstAsync(p => p.Name == "Test Product");
        dbContext.DecryptLoadedEntities(); // Decrypt the loaded entities
        savedProduct.Description.Should().Be("Public product description");
        savedProduct.InternalNotes.Should().Be("Confidential internal notes about pricing strategy");
        savedProduct.Cost.Should().Be(89.50m);
        savedProduct.SupplierInfo.Should().Be("Confidential supplier details and contracts");
        savedProduct.Price.Should().Be(199.99m);
    }

    [Test]
    public async Task Should_Handle_Bulk_Operations()
    {
        // Arrange
        var dbContext = _scope?.ServiceProvider.GetRequiredService<TestDbContext>();
        dbContext.Should().NotBeNull();

        var users = Enumerable.Range(1, 10).Select(i => new User
        {
            Username = $"bulk_user_{i}",
            Email = $"bulk{i}@example.com",
            CreditCardNumber = $"1234-5678-9012-{i:D4}",
            SocialSecurityNumber = $"{i:D3}-45-6789",
            Salary = 50000 + (i * 1000),
            Notes = $"Bulk user {i} confidential notes"
        }).ToArray();

        // Act
        dbContext!.Users.AddRange(users);
        await dbContext.SaveChangesAsync();

        // Assert
        var savedUsers = await dbContext.Users
            .Where(u => u.Username.StartsWith("bulk_user_"))
            .OrderBy(u => u.Id) // Order by ID instead of username to avoid string sorting issues
            .ToListAsync();
        dbContext.DecryptLoadedEntities(); // Decrypt the loaded entities

        savedUsers.Should().HaveCount(10);

        // Check each user (note: they might not be in the exact order 1,2,3... due to database insertion)
        foreach (var user in savedUsers)
        {
            // Extract the user number from the username
            var userNumber = int.Parse(user.Username.Split('_')[2]);
            user.Username.Should().Be($"bulk_user_{userNumber}");
            user.CreditCardNumber.Should().Be($"1234-5678-9012-{userNumber:D4}");
            user.SocialSecurityNumber.Should().Be($"{userNumber:D3}-45-6789");
            user.Notes.Should().Be($"Bulk user {userNumber} confidential notes");
        }
    }

    // Helper method to get raw (encrypted) values from the database
    private static async Task<RawUserData?> GetRawDatabaseValues(TestDbContext dbContext, int userId)
    {
        var sql = @"
            SELECT Username, Email, CreditCardNumber, SocialSecurityNumber, Notes
            FROM Users 
            WHERE Id = {0}";

        var rawData = await dbContext.Database.SqlQueryRaw<RawUserData>(sql, userId)
            .FirstOrDefaultAsync();

        return rawData;
    }

    // Helper class to capture raw database values (encrypted)
    // These properties are used by Entity Framework's SqlQueryRaw method for mapping
    private sealed class RawUserData
    {
        public string Username { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        // Initialized to satisfy SonarQube warnings - actual values set by EF mapping
        public string? CreditCardNumber { get; init; } = null;
        public string SocialSecurityNumber { get; init; } = string.Empty;
        // Initialized to satisfy SonarQube warnings - actual values set by EF mapping  
        public string? Notes { get; init; } = null;
    }
}

// Test Data Models
public class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Encrypted] // This field will be encrypted
    [MaxLength(255)]
    public string? CreditCardNumber { get; set; }

    [Encrypted] // This field will be encrypted
    [Required]
    [MaxLength(255)]
    public string SocialSecurityNumber { get; set; } = string.Empty;

    public DateTime DateOfBirth { get; set; }

    [Encrypted] // Encrypted decimal field
    [Column(TypeName = "decimal(18,2)")]
    public decimal Salary { get; set; }

    public bool IsActive { get; set; } = true;

    [Encrypted] // Encrypted text field
    public string? Notes { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}

public class Product
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Encrypted] // Encrypted long text
    public string? InternalNotes { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [Encrypted] // Encrypted cost information
    [Column(TypeName = "decimal(18,2)")]
    public decimal Cost { get; set; }

    [Encrypted] // Encrypted supplier information
    public string? SupplierInfo { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}

// Test DbContext
public class TestDbContext : DbContext
{
    private readonly IEncryptionService? _encryptionService;

    public TestDbContext(DbContextOptions<TestDbContext> options, IEncryptionService? encryptionService = null) 
        : base(options)
    {
        _encryptionService = encryptionService;
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Product> Products { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // Configure Product entity
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Configure column encryption if service is available
        if (_encryptionService != null)
        {
            modelBuilder.UseColumnEncryption(_encryptionService);
        }
    }

    public override int SaveChanges()
    {
        if (_encryptionService != null)
        {
            this.ProcessEncryption(_encryptionService);
        }
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_encryptionService != null)
        {
            this.ProcessEncryption(_encryptionService);
        }
        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Decrypts loaded entities
    /// </summary>
    public void DecryptLoadedEntities()
    {
        if (_encryptionService != null)
        {
            this.ProcessDecryption(_encryptionService);
        }
    }
}