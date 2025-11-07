# ColumnEncryptor

A robust .NET library for encrypting Entity Framework Core database columns using AES-GCM encryption with Azure Key Vault and HashiCorp Vault integration.

## 🚀 Quick Start

### Installation

```bash
dotnet add package ColumnEncryptor
```

### Basic Usage

**1. Configure services:**

```csharp
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

await app.Services.InitializeEncryptionKeysAsync();
```

**2. Mark properties for encryption:**

```csharp
using ColumnEncryptor.Attributes;

public class User
{
    public int Id { get; set; }
    
    [Encrypted]
    public string Email { get; set; } = string.Empty;
    
    [Encrypted]
    public string SocialSecurityNumber { get; set; } = string.Empty;
}
```

**3. Configure DbContext:**

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

## ✨ Features

- **🔒 AES-GCM 256-bit encryption** with per-row security
- **🔑 Azure Key Vault & HashiCorp Vault** support
- **🔄 Key rotation** without data re-encryption
- **🎯 Simple attribute-based** configuration
- **⚡ Performance optimized** with key caching
- **🛡️ Production ready** with comprehensive logging

## 📖 Key Management

### Azure Key Vault

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

### HashiCorp Vault

```csharp
services.AddColumnEncryption(options =>
{
    options.KeyProvider = KeyProviderType.HashiCorpVault;
    options.HashiCorpVault = new HashiCorpVaultOptions
    {
        VaultUrl = "https://vault.example.com",
        Token = "your-vault-token",
        SecretPath = "secret/data/encryption-keys"
    };
});
```

## 🔐 Security

- AES-GCM with 256-bit keys
- Unique nonce per encryption operation
- Authentication tags for integrity verification
- Secure key storage in external vaults
- No plaintext keys in application memory

## 📝 License

Licensed under the MIT License. See [LICENSE](https://github.com/Tricksfor-Organization/ColumnEncryptor/blob/main/LICENSE) for details.

## 🤝 Contributing

Contributions are welcome! Please visit the [GitHub repository](https://github.com/Tricksfor-Organization/ColumnEncryptor) for more information.

## 📚 Documentation

For detailed documentation and advanced scenarios, visit:
- [Full Documentation](https://github.com/Tricksfor-Organization/ColumnEncryptor)
- [Usage Guide](https://github.com/Tricksfor-Organization/ColumnEncryptor/blob/main/USAGE.md)
- [Examples](https://github.com/Tricksfor-Organization/ColumnEncryptor/tree/main/examples)
