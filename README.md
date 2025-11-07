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

## Configuration via appsettings.json

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

```csharp
services.AddColumnEncryption(configuration.GetSection("ColumnEncryption"));
```

## Security Features

- **Key Rotation**: Change encryption keys without re-encrypting existing data
- **Key Versioning**: Each encrypted value includes key metadata for seamless decryption
- **Secure Storage**: Keys stored in Azure Key Vault or HashiCorp Vault
- **Authentication**: Multiple authentication methods (Managed Identity, Service Principal, DefaultAzureCredential, etc.)
- **Audit Logging**: Comprehensive logging for security monitoring

## Documentation

- [Usage Guide](USAGE.md) - Comprehensive setup and usage examples
- [Security Best Practices](#security-considerations)
- [Migration Guide](#migration-from-local-to-vault)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
