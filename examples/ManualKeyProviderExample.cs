using ColumnEncryptor;
using ColumnEncryptor.Attributes;
using ColumnEncryptor.Extensions;
using ColumnEncryptor.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace ColumnEncryptorExamples;

/// <summary>
/// Example demonstrating how to use the Manual Key Provider for column encryption
/// This is suitable for development, testing, or scenarios where you manage keys manually
/// </summary>
public class ManualKeyProviderExample
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== ColumnEncryptor - Manual Key Provider Example ===\n");

        // Step 1: Setup DI Container
        var services = new ServiceCollection();
        services.AddLogging(builder => 
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Step 2: Generate a secure encryption key (32 bytes for AES-256)
        var encryptionKeyBytes = RandomNumberGenerator.GetBytes(32);
        var encryptionKeyBase64 = Convert.ToBase64String(encryptionKeyBytes);
        
        Console.WriteLine($"Generated encryption key: {encryptionKeyBase64}");
        Console.WriteLine("⚠️  IMPORTANT: Store this key securely! Never commit it to source control.\n");

        // Step 3: Configure column encryption with Manual provider
        services.AddColumnEncryption(options =>
        {
            options.KeyProvider = KeyProviderType.Manual;
            options.Manual = new ManualKeyProviderOptions
            {
                PrimaryKeyId = "primary-key",
                Keys = new List<ManualEncryptionKey>
                {
                    new ManualEncryptionKey 
                    { 
                        Id = "primary-key", 
                        KeyBase64 = encryptionKeyBase64,
                        CreatedUtc = DateTime.UtcNow
                    }
                }
            };
        });

        // Step 4: Add DbContext
        services.AddDbContext<SampleDbContext>(options =>
        {
            options.UseInMemoryDatabase("ManualProviderExample");
        });

        var serviceProvider = services.BuildServiceProvider();

        // Step 5: Use the encryption service
        Console.WriteLine("\n--- Testing Encryption Service ---");
        var encryptionService = serviceProvider.GetRequiredService<IEncryptionService>();
        
        var plainText = "sensitive-data@example.com";
        var encrypted = encryptionService.Encrypt(plainText);
        var decrypted = encryptionService.Decrypt(encrypted);
        
        Console.WriteLine($"Plain text:  {plainText}");
        Console.WriteLine($"Encrypted:   {encrypted[..50]}..."); // Show first 50 chars
        Console.WriteLine($"Decrypted:   {decrypted}");
        Console.WriteLine($"Match:       {plainText == decrypted}\n");

        // Step 6: Use with Entity Framework
        Console.WriteLine("--- Testing with Entity Framework ---");
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            
            // Insert a user with encrypted fields
            var user = new User
            {
                Id = 1,
                Username = "john_doe",
                Email = "john.doe@example.com",
                SocialSecurityNumber = "123-45-6789"
            };
            
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
            Console.WriteLine($"Created user: {user.Username}");
        }

        // Retrieve and verify
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == 1);
            
            if (user != null)
            {
                Console.WriteLine($"Retrieved user: {user.Username}");
                Console.WriteLine($"Email (decrypted): {user.Email}");
                Console.WriteLine($"SSN (decrypted): {user.SocialSecurityNumber}\n");
            }
        }

        // Step 7: Demonstrate key rotation
        Console.WriteLine("--- Demonstrating Key Rotation ---");
        var keyProvider = serviceProvider.GetRequiredService<IKeyProvider>();
        
        // Add a new key
        var newKeyBytes = RandomNumberGenerator.GetBytes(32);
        var newKey = new ColumnEncryptor.Common.EncryptionKey("rotated-key", newKeyBytes, DateTime.UtcNow);
        keyProvider.AddKey(newKey);
        Console.WriteLine("Added new key: rotated-key");
        
        // Promote new key to primary
        keyProvider.PromoteKey("rotated-key");
        Console.WriteLine("Promoted rotated-key to primary");
        
        var currentPrimary = keyProvider.GetPrimaryKey();
        Console.WriteLine($"Current primary key: {currentPrimary.Id}\n");

        Console.WriteLine("=== Example Complete ===");
        Console.WriteLine("\n💡 Tips:");
        Console.WriteLine("  - Use environment variables for production keys");
        Console.WriteLine("  - Consider Azure Key Vault or HashiCorp Vault for production");
        Console.WriteLine("  - Rotate keys periodically for enhanced security");
        Console.WriteLine("  - Never commit encryption keys to source control");
    }
}

// Sample entity with encrypted fields
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    
    [Encrypted]
    public string Email { get; set; } = string.Empty;
    
    [Encrypted]
    public string SocialSecurityNumber { get; set; } = string.Empty;
}

// Sample DbContext
public class SampleDbContext : DbContext
{
    private readonly IEncryptionService _encryptionService;

    public SampleDbContext(
        DbContextOptions<SampleDbContext> options,
        IEncryptionService encryptionService) : base(options)
    {
        _encryptionService = encryptionService;
    }

    public DbSet<User> Users { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseColumnEncryption(_encryptionService);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        this.ProcessEncryption(_encryptionService);
        return await base.SaveChangesAsync(cancellationToken);
    }
}
