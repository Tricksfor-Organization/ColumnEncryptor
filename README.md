 and replace it wi# Tricksfor.ColumnEncryptor

Transparent column-level encryption for Entity Framework Core with enterprise key management.

## Features

- **🔒 Strong Encryption**: AES-GCM 256-bit encryption with per-row encryption
- **🔑 Key Management**: Azure Key Vault and HashiCorp Vault support
- **🔄 Key Rotation**: Seamless key versioning without data re-encryption
- **🎯 Attribute-Based**: Simple `[Encrypted]` attribute
- **⚡ Performance**: Built-in key caching
- **️ Production Ready**: Comprehensive logging and error handling

## Quick Start

### 1. Installation

```bash
dotnet add package Tricksfor.ColumnEncryptor
```

### 2. Configure Services

```csharp
// Program.cs with Azure Key Vault
services.AddColumnEncryption(options =>
{
    options.KeyProvider = KeyProviderType.AzureKeyVault;
    options.AzureKeyVault = new AzureKeyVaultOptions
    {
        VaultUrl = "https://your-keyvault.vault.azure.net/",
        AuthMethod = AzureAuthMethod.DefaultAzureCredential,
        KeyPrefix = "myapp-encryption-keys"
    };
});

// Initialize encryption keys
await app.Services.InitializeEncryptionKeysAsync();
```

### 3. Mark Properties for Encryption

```csharp
using ColumnEncryptor.Attributes;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    
    [Encrypted]
    public string Email { get; set; } = string.Empty;
    
    [Encrypted]
    public string SocialSecurityNumber { get; set; } = string.Empty;
}
```

### 4. Configure DbContext

```csharp
using ColumnEncryptor.Extensions;

public class ApplicationDbContext : DbContext
{
    private readonly IEncryptionService _encryptionService;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IEncryptionService encryptionService) : base(options)
    {
        _encryptionService = encryptionService;
    }

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
```

## Key Management Options

### Azure Key Vault Provider (Recommended)

```csharp
services.AddColumnEncryption(options =>
{
    options.KeyProvider = KeyProviderType.AzureKeyVault;
    options.AzureKeyVault = new AzureKeyVaultOptions
    {
        VaultUrl = "https://your-keyvault.vault.azure.net/",
        AuthMethod = AzureAuthMethod.DefaultAzureCredential,
        KeyPrefix = "encryption-keys"
    };
});
```

### HashiCorp Vault Provider

```csharp
services.AddColumnEncryption(options =>
{
    options.KeyProvider = KeyProviderType.HashiCorpVault;
    options.Vault = new VaultOptions
    {
        ServerUrl = "https://vault.example.com:8200",
        AuthMethod = VaultAuthMethod.AppRole,
        RoleId = "your-role-id",
        SecretId = "your-secret-id",
        KeysPath = "secret/encryption-keys"
    };
});
```

### Manual Provider (Configuration-Based)

For development, testing, or scenarios where you want to manage keys manually without external vault infrastructure:

```csharp
services.AddColumnEncryption(options =>
{
    options.KeyProvider = KeyProviderType.Manual;
    options.Manual = new ManualKeyProviderOptions
    {
        PrimaryKeyId = "key-1",
        Keys = new List<ManualEncryptionKey>
        {
            new ManualEncryptionKey 
            { 
                Id = "key-1", 
                KeyBase64 = "YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXoxMjM0NTY=" // 32-byte key in Base64
            }
        }
    };
});
```

**⚠️ Security Warning**: The Manual provider stores encryption keys directly in your application configuration. This is suitable for:
- Development and testing environments
- Non-production scenarios
- Applications with existing secure configuration management

**Never commit encryption keys to source control.** Use environment variables, Azure App Configuration, or secure secret management for production.

**Generate secure keys:**
```bash
# Linux/macOS
openssl rand -base64 32

# PowerShell
[Convert]::ToBase64String((New-Object byte[] 32 | ForEach-Object { [System.Security.Cryptography.RandomNumberGenerator]::Fill($_); $_ }))
```

## Advanced Configuration

### Using IServiceProvider for Dynamic Configuration

You can access the service provider during configuration to fetch settings from other services:

```csharp
// Example 1: Using IOptions to fetch configuration
services.AddColumnEncryption((options, sp) =>
{
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

// Example 2: Environment-based configuration
services.AddColumnEncryption((options, sp) =>
{
    var environment = sp.GetRequiredService<IHostEnvironment>();
    
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
```

## Configuration via appsettings.json

### Azure Key Vault

```json
{
  "ColumnEncryption": {
    "KeyProvider": "AzureKeyVault",
    "AzureKeyVault": {
      "VaultUrl": "https://your-keyvault.vault.azure.net/",
      "AuthMethod": "DefaultAzureCredential",
      "KeyPrefix": "myapp-encryption-keys",
      "CacheExpiryMinutes": 5
    }
  }
}
```

### Manual Provider

```json
{
  "ColumnEncryption": {
    "KeyProvider": "Manual",
    "Manual": {
      "PrimaryKeyId": "key-1",
      "Keys": [
        {
          "Id": "key-1",
          "KeyBase64": "YOUR-BASE64-ENCODED-32-BYTE-KEY-HERE",
          "CreatedUtc": "2024-01-01T00:00:00Z"
        }
      ]
    }
  }
}
```

Then bind the configuration:

```csharp
var encryptionOptions = new EncryptionOptions();
configuration.GetSection("ColumnEncryption").Bind(encryptionOptions);
services.AddColumnEncryptor(encryptionOptions);
```

Or use the service provider overload:

```csharp
services.AddColumnEncryption((options, sp) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    configuration.GetSection("ColumnEncryption").Bind(options);
});
```

## Security Features

- **Key Rotation**: Change encryption keys without re-encrypting existing data
- **Key Versioning**: Each encrypted value includes key metadata for seamless decryption
- **Secure Storage**: Keys stored in Azure Key Vault, HashiCorp Vault, or managed manually
- **Authentication**: Multiple authentication methods (Managed Identity, Service Principal, DefaultAzureCredential, etc.)
- **Audit Logging**: Comprehensive logging for security monitoring

## Documentation

- [Usage Guide](USAGE.md) - Comprehensive setup and usage examples
- [Security Best Practices](#security-considerations)
- [Migration Guide](#migration-from-local-to-vault)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
