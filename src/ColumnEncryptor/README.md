# Tricksfor.ColumnEncryptor

Transparent column-level encryption for Entity Framework Core with Azure Key Vault and HashiCorp Vault support.

## Installation

```bash
dotnet add package Tricksfor.ColumnEncryptor
```

## Quick Start

**1. Configure services:**

```csharp
services.AddColumnEncryption(options =>
{
    options.KeyProvider = KeyProviderType.AzureKeyVault;
    options.AzureKeyVault = new AzureKeyVaultOptions
    {
        VaultUrl = "https://your-keyvault.vault.azure.net/",
        AuthMethod = AzureAuthMethod.DefaultAzureCredential
    };
});

await app.Services.InitializeEncryptionKeysAsync();
```

**2. Mark properties:**

```csharp
public class User
{
    public int Id { get; set; }
    
    [Encrypted]
    public string Email { get; set; } = string.Empty;
}
```

**3. Configure DbContext:**

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UseColumnEncryption(_encryptionService);
}

public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    this.ProcessEncryption(_encryptionService);
    return await base.SaveChangesAsync(ct);
}
```

## Features

- 🔒 AES-GCM 256-bit encryption
- 🔑 Azure Key Vault & HashiCorp Vault
- 🔄 Key rotation support
- ⚡ Performance optimized
- 🎯 Attribute-based configuration

## Documentation

- [Full Documentation](https://github.com/Tricksfor-Organization/ColumnEncryptor)
- [Usage Guide](https://github.com/Tricksfor-Organization/ColumnEncryptor/blob/main/USAGE.md)

## License

MIT License - see [LICENSE](https://github.com/Tricksfor-Organization/ColumnEncryptor/blob/main/LICENSE)
