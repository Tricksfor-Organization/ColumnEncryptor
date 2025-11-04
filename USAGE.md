# Column Encryptor Usage Guide

This guide shows how to configure and use the Column Encryptor library with Azure Key Vault and HashiCorp Vault key management.

## Configuration

### 1. Azure Key Vault Provider (Recommended)

#### Basic Configuration with DefaultAzureCredential

```csharp
// Program.cs or Startup.cs
services.AddColumnEncryption(options =>
{
    options.KeyProvider = KeyProviderType.AzureKeyVault;
    options.AzureKeyVault = new AzureKeyVaultOptions
    {
        VaultUrl = "https://your-keyvault.vault.azure.net/",
        AuthMethod = AzureAuthMethod.DefaultAzureCredential,
        KeyPrefix = "myapp-encryption-keys",
        CacheExpiryMinutes = 5
    };
});

// Initialize encryption keys
await app.Services.InitializeEncryptionKeysAsync();
```

#### Service Principal Authentication

```csharp
services.AddColumnEncryption(options =>
{
    options.KeyProvider = KeyProviderType.AzureKeyVault;
    options.AzureKeyVault = new AzureKeyVaultOptions
    {
        VaultUrl = "https://your-keyvault.vault.azure.net/",
        AuthMethod = AzureAuthMethod.ServicePrincipal,
        TenantId = configuration["AzureKeyVault:TenantId"],
        ClientId = configuration["AzureKeyVault:ClientId"],
        ClientSecret = configuration["AzureKeyVault:ClientSecret"],
        KeyPrefix = "encryption-keys",
        CacheExpiryMinutes = 5
    };
});
```

#### Managed Identity Authentication

```csharp
services.AddColumnEncryption(options =>
{
    options.KeyProvider = KeyProviderType.AzureKeyVault;
    options.AzureKeyVault = new AzureKeyVaultOptions
    {
        VaultUrl = "https://your-keyvault.vault.azure.net/",
        AuthMethod = AzureAuthMethod.ManagedIdentity,
        ClientId = configuration["AzureKeyVault:ClientId"], // Optional for user-assigned MI
        KeyPrefix = "encryption-keys"
    };
});
```

### 2. HashiCorp Vault Provider

```csharp
services.AddColumnEncryption(options =>
{
    options.KeyProvider = KeyProviderType.HashiCorpVault;
    options.Vault = new VaultOptions
    {
        ServerUrl = "https://vault.example.com:8200",
        AuthMethod = VaultAuthMethod.AppRole,
        RoleId = configuration["Vault:RoleId"],
        SecretId = configuration["Vault:SecretId"],
        KeysPath = "secret/myapp/encryption-keys",
        Namespace = "myapp", // Optional for Vault Enterprise
        CacheExpiryMinutes = 5
    };
});
```

### 3. Configuration via appsettings.json

#### Azure Key Vault Configuration

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

#### HashiCorp Vault Configuration

```json
{
  "ColumnEncryption": {
    "KeyProvider": "HashiCorpVault",
    "Vault": {
      "ServerUrl": "https://vault.example.com:8200",
      "AuthMethod": "Token",
      "Token": "hvs.your-vault-token",
      "KeysPath": "secret/myapp/encryption-keys",
      "CacheExpiryMinutes": 5
    }
  }
}
```

## Entity Configuration

### 1. Mark Properties for Encryption

```csharp
using ColumnEncryptor.Attributes;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    
    [Encrypted]
    public string Email { get; set; } = string.Empty;
    
    [Encrypted]
    public string PhoneNumber { get; set; } = string.Empty;
    
    // Sensitive personal information
    [Encrypted]
    public string SocialSecurityNumber { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; }
}
```

### 2. Configure DbContext

```csharp
using ColumnEncryptor.Extensions;
using ColumnEncryptor.Interfaces;
using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : DbContext
{
    private readonly IEncryptionService _encryptionService;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
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

    public override int SaveChanges()
    {
        // Process encryption before saving
        this.ProcessEncryption(_encryptionService);
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Process encryption before saving
        this.ProcessEncryption(_encryptionService);
        return await base.SaveChangesAsync(cancellationToken);
    }
}
```

## Azure Key Vault Setup

### 1. Create Azure Key Vault

```bash
# Create resource group
az group create --name myapp-rg --location eastus

# Create Key Vault
az keyvault create --name myapp-keyvault --resource-group myapp-rg --location eastus

# Get the Vault URL
az keyvault show --name myapp-keyvault --query properties.vaultUri -o tsv
```

### 2. Configure Access Policies

#### For Service Principal

```bash
# Create service principal
az ad sp create-for-rbac --name myapp-encryption --skip-assignment

# Grant Key Vault permissions
az keyvault set-policy --name myapp-keyvault \
    --spn <service-principal-id> \
    --secret-permissions get set list delete
```

#### For Managed Identity

```bash
# Create user-assigned managed identity
az identity create --name myapp-encryption-identity --resource-group myapp-rg

# Grant Key Vault permissions
az keyvault set-policy --name myapp-keyvault \
    --object-id <managed-identity-principal-id> \
    --secret-permissions get set list delete
```

### 3. Using DefaultAzureCredential

DefaultAzureCredential tries authentication methods in this order:
1. EnvironmentCredential (Service Principal via environment variables)
2. ManagedIdentityCredential (if running on Azure)
3. SharedTokenCacheCredential (Visual Studio/Azure CLI)
4. InteractiveBrowserCredential (development scenarios)

## HashiCorp Vault Setup

### 1. Enable KV Secrets Engine

```bash
vault secrets enable -path=secret kv-v2
```

### 2. Create AppRole Authentication

```bash
# Enable AppRole auth method
vault auth enable approle

# Create a policy for encryption keys
vault policy write encryption-keys - <<EOF
path "secret/data/myapp/encryption-keys/*" {
  capabilities = ["create", "read", "update", "delete", "list"]
}
path "secret/metadata/myapp/encryption-keys/*" {
  capabilities = ["list", "delete"]
}
EOF

# Create an AppRole
vault write auth/approle/role/myapp-encryption \
    token_policies="encryption-keys" \
    token_ttl=1h \
    token_max_ttl=4h

# Get RoleID and SecretID
vault read auth/approle/role/myapp-encryption/role-id
vault write -f auth/approle/role/myapp-encryption/secret-id
```

## Manual Encryption/Decryption

```csharp
public class UserService
{
    private readonly IEncryptionService _encryptionService;
    private readonly ApplicationDbContext _context;

    public UserService(IEncryptionService encryptionService, ApplicationDbContext context)
    {
        _encryptionService = encryptionService;
        _context = context;
    }

    public async Task<User> CreateUserAsync(string username, string email)
    {
        var user = new User
        {
            Username = username,
            Email = email, // Will be automatically encrypted when saved
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(); // Encryption happens here

        return user;
    }

    public string EncryptSensitiveData(string plaintext)
    {
        return _encryptionService.Encrypt(plaintext);
    }

    public string DecryptSensitiveData(string encryptedData)
    {
        return _encryptionService.Decrypt(encryptedData);
    }
}
```

## Key Management

### Initialize Keys Programmatically

```csharp
public class KeyManagementService
{
    private readonly IKeyProvider _keyProvider;

    public KeyManagementService(IKeyProvider keyProvider)
    {
        _keyProvider = keyProvider;
    }

    public void CreateNewKey(string keyId = null)
    {
        var keyBytes = RandomNumberGenerator.GetBytes(32); // 256-bit key
        var key = new EncryptionKey(
            keyId ?? Guid.NewGuid().ToString("N"),
            keyBytes,
            DateTime.UtcNow
        );
        
        _keyProvider.AddKey(key);
    }

    public void RotateToNewPrimaryKey(string newKeyId)
    {
        _keyProvider.PromoteKey(newKeyId);
    }

    public IEnumerable<EncryptionKey> GetAllKeys()
    {
        return _keyProvider.GetAllKeys();
    }
}
```

## Security Considerations

### Azure Key Vault

1. **Access Policies**: Use least privilege principle
2. **Managed Identity**: Prefer over Service Principal when possible
3. **Key Rotation**: Regularly rotate encryption keys
4. **Audit Logging**: Enable Key Vault audit logging
5. **Network Security**: Use Private Endpoints when possible
6. **Backup**: Enable soft delete and purge protection

### HashiCorp Vault

1. **Access Control**: Use Vault policies to restrict key access
2. **Audit**: Enable Vault audit logging
3. **Network Security**: Use TLS for Vault communication
4. **Backup**: Ensure Vault backups include your encryption keys
5. **Disaster Recovery**: Have a plan for key recovery

## Performance Considerations

1. **Key Caching**: Keys are cached for 5 minutes by default
2. **Connection Pooling**: HTTP clients are reused efficiently
3. **Async Operations**: Use async methods where available
4. **Batch Operations**: Process multiple entities efficiently

## Troubleshooting

### Azure Key Vault Issues

1. **Authentication Failed**: Check credential configuration and permissions
2. **Key Not Found**: Verify key exists in Key Vault and cache is refreshed
3. **Access Denied**: Check Key Vault access policies
4. **Network Issues**: Verify Key Vault URL and network connectivity

### HashiCorp Vault Issues

1. **Vault Connection Failed**: Check network connectivity and authentication
2. **Key Not Found**: Verify key exists in Vault and cache is refreshed
3. **Permission Denied**: Verify Vault policies allow key operations

### Logging

Enable detailed logging for troubleshooting:

```json
{
  "Logging": {
    "LogLevel": {
      "ColumnEncryptor": "Debug",
      "Azure.Security.KeyVault": "Information",
      "Azure.Identity": "Information"
    }
  }
}
```