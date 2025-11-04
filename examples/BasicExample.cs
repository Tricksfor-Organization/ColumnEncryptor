using ColumnEncryptor;
using ColumnEncryptor.Attributes;
using ColumnEncryptor.Extensions;
using ColumnEncryptor.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Example entity with encrypted properties
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    
    [Encrypted]
    public string Email { get; set; } = string.Empty;
    
    [Encrypted]
    public string PhoneNumber { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; }
}

// Example DbContext with encryption
public class ExampleDbContext : DbContext
{
    private readonly IEncryptionService _encryptionService;

    public ExampleDbContext(
        DbContextOptions<ExampleDbContext> options,
        IEncryptionService encryptionService) : base(options)
    {
        _encryptionService = encryptionService;
    }

    public DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Configure automatic encryption/decryption
        modelBuilder.UseColumnEncryption(_encryptionService);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Process encryption before saving
        this.ProcessEncryption(_encryptionService);
        return await base.SaveChangesAsync(cancellationToken);
    }
}

// Example usage
public class Program
{
    public static async Task Main(string[] args)
    {
        // Setup DI container
        var services = new ServiceCollection();
        
        // Configure logging
        services.AddLogging(builder => builder.AddConsole());
        
        // Configure column encryption with Azure Key Vault for demo
        services.AddColumnEncryption(options =>
        {
            options.KeyProvider = KeyProviderType.AzureKeyVault;
            options.AzureKeyVault = new AzureKeyVaultOptions
            {
                VaultUrl = "https://your-keyvault.vault.azure.net/",
                AuthMethod = AzureAuthMethod.DefaultAzureCredential,
                KeyPrefix = "demo-encryption-keys"
            };
        });
        
        // Configure EF Core with SQLite for demo
        services.AddDbContext<ExampleDbContext>(options =>
            options.UseSqlite("Data Source=demo.db"));
        
        // Build service provider
        var serviceProvider = services.BuildServiceProvider();
        
        // Initialize encryption keys
        await serviceProvider.InitializeEncryptionKeysAsync();
        
        // Example usage
        await using var scope = serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExampleDbContext>();
        
        // Ensure database is created
        await context.Database.EnsureCreatedAsync();
        
        // Create a new user with sensitive data
        var user = new User
        {
            Username = "johndoe",
            Email = "john.doe@example.com",        // This will be encrypted
            PhoneNumber = "+1-555-123-4567",       // This will be encrypted
            CreatedAt = DateTime.UtcNow
        };
        
        context.Users.Add(user);
        await context.SaveChangesAsync(); // Encryption happens here
        
        Console.WriteLine($"Created user with ID: {user.Id}");
        Console.WriteLine($"Username (not encrypted): {user.Username}");
        Console.WriteLine($"Email (decrypted): {user.Email}");
        Console.WriteLine($"Phone (decrypted): {user.PhoneNumber}");
        
        // Demonstrate that data is actually encrypted in the database
        var encryptionService = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
        var rawEmailFromDb = await context.Database
            .SqlQueryRaw<string>("SELECT Email FROM Users WHERE Id = {0}", user.Id)
            .FirstAsync();
        
        Console.WriteLine($"\nRaw encrypted email in database: {rawEmailFromDb}");
        Console.WriteLine($"Decrypted email: {encryptionService.Decrypt(rawEmailFromDb)}");
        
        // Demonstrate key management
        var keyProvider = scope.ServiceProvider.GetRequiredService<IKeyProvider>();
        var allKeys = keyProvider.GetAllKeys();
        var primaryKey = keyProvider.GetPrimaryKey();
        
        Console.WriteLine($"\nTotal encryption keys: {allKeys.Count()}");
        Console.WriteLine($"Primary key ID: {primaryKey.Id}");
        Console.WriteLine($"Primary key created: {primaryKey.CreatedUtc}");
    }
}