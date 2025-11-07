# GitHub Copilot Instructions for ColumnEncryptor

## Project Overview
ColumnEncryptor is a production-ready .NET library that provides transparent column-level encryption for Entity Framework Core applications. It implements AES-GCM 256-bit encryption with enterprise key management support via Azure Key Vault and HashiCorp Vault.

## Technology Stack
- **Target Framework**: .NET 9.0
- **Primary Dependencies**: 
  - Entity Framework Core 9.0.10
  - Azure.Security.KeyVault.Secrets 4.6.0
  - Azure.Identity 1.12.1
- **Testing**: NUnit 3.x with Testcontainers for integration tests
- **Package Management**: Central Package Management (CPM) via Directory.Packages.props

## Architecture & Design Patterns

### Core Components
1. **Encryption Service** (`IEncryptionService`): Handles AES-GCM encryption/decryption with JSON payload format
2. **Key Providers** (`IKeyProvider`): Abstract key storage with implementations for:
   - Azure Key Vault (`AzureKeyVaultProvider`)
   - HashiCorp Vault (`VaultKeyProvider`)
3. **Vault Clients** (`IVaultClient`): HTTP communication layer for vault systems
4. **EF Core Extensions**: Automatic encryption/decryption hooks via `DbContextExtensions`

### Key Design Principles
- **Encryption Format**: JSON payload containing `{Version, KeyId, Nonce, CipherText, Tag}`
- **Key Rotation**: Support multiple key versions; encrypted data stores KeyId for decryption
- **Per-Row Encryption**: Each encrypted value uses a unique nonce (12 bytes for AES-GCM)
- **Caching Strategy**: Local key caching with configurable expiry (default: 5 minutes)
- **Fail-Safe Decryption**: Gracefully handle decryption failures for legacy/unencrypted data

## Coding Standards

### C# Style Guidelines
1. **Nullable Reference Types**: Enabled project-wide; always use proper null annotations
   ```csharp
   public string? OptionalValue { get; set; }  // Nullable
   public string RequiredValue { get; set; } = string.Empty;  // Non-nullable with default
   ```

2. **Primary Constructors**: Use for dependency injection in services (C# 12 feature)
   ```csharp
   public class AesGcmEncryptionService(IKeyProvider keyProvider) : IEncryptionService
   {
       private readonly IKeyProvider _keyProvider = keyProvider;
   }
   ```

3. **Record Types**: Use for immutable data structures
   ```csharp
   public record EncryptionKey(string Id, byte[] KeyBytes, DateTime CreatedUtc);
   ```

4. **Attributes**: Keep simple marker attributes minimal
   ```csharp
   [AttributeUsage(AttributeTargets.Property)]
   public sealed class EncryptedAttribute : Attribute { }
   ```

### Security Best Practices
1. **Cryptographic Operations**:
   - Always use `System.Security.Cryptography` namespace
   - Generate nonces with `RandomNumberGenerator.GetBytes(12)` for AES-GCM
   - Use 16-byte authentication tags for AES-GCM
   - Dispose crypto objects properly with `using` statements

2. **Secret Management**:
   - Never hardcode credentials or tokens
   - Support multiple authentication methods (DefaultAzureCredential, ServicePrincipal, ManagedIdentity, Token, AppRole)
   - Store secrets only in vault systems, never in code or configuration

3. **Key Handling**:
   - Keys are always Base64-encoded when serialized
   - Support key versioning via KeyId in encrypted payload
   - Implement thread-safe key cache with locks

### Dependency Injection Patterns
1. **Service Registration**: Use extension methods on `IServiceCollection`
   ```csharp
   public static IServiceCollection AddColumnEncryption(
       this IServiceCollection services,
       Action<EncryptionOptions> configure)
   ```

2. **Options Pattern**: Always use `IOptions<T>` for configuration
   ```csharp
   public VaultKeyProvider(
       IVaultClient vaultClient, 
       IOptions<VaultOptions> vaultOptions,
       ILogger<VaultKeyProvider> logger)
   ```

3. **Singleton Services**: Encryption services should be registered as singletons for performance

### Entity Framework Integration
1. **Attribute Detection**: Use reflection to find `[Encrypted]` attributes
   ```csharp
   var encryptedProperties = entityType.ClrType
       .GetProperties()
       .Where(p => p.GetCustomAttribute<EncryptedAttribute>() != null && p.PropertyType == typeof(string));
   ```

2. **Change Tracking**: Hook into EF Core's `ChangeTracker` for automatic encryption
   ```csharp
   var encryptedEntities = context.ChangeTracker.Entries()
       .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);
   ```

3. **JSON Detection**: Check if value is already encrypted by looking for JSON structure
   ```csharp
   private static bool IsAlreadyEncrypted(string value)
   {
       return value.TrimStart().StartsWith('{') && value.TrimEnd().EndsWith('}');
   }
   ```

## Testing Guidelines

### Test Structure
1. **Integration Tests**: Use Testcontainers for real infrastructure (SQL Server, HashiCorp Vault)
2. **Test Fixtures**: Use `[OneTimeSetUp]` for expensive container initialization
3. **Assertions**: Use FluentAssertions for readable test assertions
4. **Mocking**: Use NSubstitute for unit testing interfaces

### Container Testing Pattern
```csharp
// Start container with random port to avoid conflicts
_vaultContainer = new ContainerBuilder()
    .WithImage(VaultImage)
    .WithPortBinding(VaultPort, true)
    .WithCommand("server", "-dev", $"-dev-root-token-id={VaultToken}")
    .Build();

await _vaultContainer.StartAsync();
var mappedPort = _vaultContainer.GetMappedPublicPort(VaultPort);
```

### Test Naming
- Use descriptive test method names: `ShouldEncryptPropertyMarkedWithEncryptedAttribute`
- Organize tests by feature area: `EntityFrameworkIntegrationTests`, `AzureKeyVaultTests`, `HashiCorpTests`

## Documentation Standards

### XML Documentation
Always document public APIs with XML comments:
```csharp
/// <summary>
/// Initializes the encryption key store with a primary key if none exists
/// Call this after building the service provider, typically in Program.cs or Startup.cs
/// </summary>
/// <param name="serviceProvider">Service provider</param>
/// <param name="keyId">Optional specific key ID to create</param>
/// <returns>Task</returns>
public static Task InitializeEncryptionKeysAsync(...)
```

### README Structure
- Quick Start section first with minimal code
- Detailed configuration options by provider type
- Security considerations and best practices
- Migration and key rotation guidance

## Common Patterns to Follow

### Error Handling
```csharp
// Validate arguments
if (key == null) throw new ArgumentNullException(nameof(key));
if (string.IsNullOrEmpty(keyId)) throw new ArgumentException("Key ID cannot be null or empty", nameof(keyId));

// Graceful degradation for decryption
try
{
    var decryptedValue = encryptionService.Decrypt(currentValue);
}
catch (Exception)
{
    // Leave value as is for legacy data
}
```

### Logging
```csharp
_logger.LogInformation("Added encryption key {KeyId} to Azure Key Vault", key.Id);
_logger.LogDebug("Refreshing key cache from Vault");
_logger.LogWarning("Key cache expired, refreshing from vault");
```

### Thread Safety
```csharp
private readonly object _lock = new();

lock (_lock)
{
    _keyCache[key.Id] = key;
    _primaryKeyId = key.Id;
}
```

## Vault Provider Implementation Guidelines

### Azure Key Vault
- Support three authentication methods: DefaultAzureCredential (recommended), ServicePrincipal, ManagedIdentity
- Use key prefixes to namespace encryption keys: `{prefix}-primary`, `{prefix}-{keyId}`
- Implement secret metadata for tracking primary key

### HashiCorp Vault
- Support Token and AppRole authentication methods
- Use KV v2 secrets engine: `/v1/secret/data/{path}`
- Handle Vault Enterprise namespaces with `X-Vault-Namespace` header
- Implement token refresh logic for long-running applications

## API Design Principles

### Fluent Configuration
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
```

### Extension Method Chaining
```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.UseColumnEncryption(_encryptionService);
}
```

### Initialization Helpers
```csharp
await app.Services.InitializeEncryptionKeysAsync();
```

## Performance Considerations

1. **Key Caching**: Always cache keys locally; vault calls are expensive
2. **Cache Expiry**: Default 5 minutes, configurable via `CacheExpiryMinutes`
3. **Lazy Refresh**: Refresh cache only when expired (`EnsureKeysAreFresh`)
4. **Bulk Operations**: Process all encrypted properties in a single pass through ChangeTracker

## Migration & Versioning

### Encryption Payload Versioning
```csharp
var payload = new EncryptedPayload
{
    Version = 1,  // Increment for breaking changes
    KeyId = key.Id,
    // ...
};

if (payload.Version != 1) throw new NotSupportedException("Unsupported encryption version");
```

### Key Rotation Process
1. Add new key: `keyProvider.AddKey(newKey)`
2. Promote to primary: `keyProvider.PromoteKey(newKeyId)`
3. Old encrypted data automatically uses correct key via stored KeyId
4. No re-encryption required for existing data

## Project-Specific Commands

### Build & Test
```bash
dotnet build ColumnEncryptor.sln
dotnet test tests/ColumnEncryptor.Tests/ColumnEncryptor.Tests.csproj
```

### Package Creation
```bash
dotnet pack src/ColumnEncryptor/ColumnEncryptor.csproj -c Release
```

## Common Pitfalls to Avoid

1. **Don't** call vault APIs synchronously in hot paths
2. **Don't** encrypt/decrypt in EF Core value converters (causes issues with query translation)
3. **Don't** forget to call `ProcessEncryption` in `SaveChangesAsync`
4. **Don't** store unencrypted sensitive data in logs or exceptions
5. **Don't** use fixed nonces (always generate random nonces per encryption)
6. **Don't** modify encrypted data directly in the database (always use the library)

## Example Usage Pattern

```csharp
// 1. Entity definition
public class User
{
    public int Id { get; set; }
    [Encrypted]
    public string Email { get; set; } = string.Empty;
}

// 2. DbContext setup
public class AppDbContext : DbContext
{
    private readonly IEncryptionService _encryptionService;
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseColumnEncryption(_encryptionService);
    }
    
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        this.ProcessEncryption(_encryptionService);
        return await base.SaveChangesAsync(ct);
    }
}

// 3. DI configuration
services.AddColumnEncryption(options => { /* ... */ });
await app.Services.InitializeEncryptionKeysAsync();
```

## When to Extend This Library

### Adding New Key Providers
1. Implement `IKeyProvider` interface
2. Implement `IVaultClient` for HTTP communication
3. Add provider registration in `Configuration.cs`
4. Add configuration class to `EncryptionOptions.cs`
5. Write integration tests with real vault instance

### Adding New Encryption Algorithms
1. Create new implementation of `IEncryptionService`
2. Update payload format with version field
3. Ensure backward compatibility with existing encrypted data
4. Add comprehensive security tests

---

**Remember**: This library handles sensitive data encryption. Always prioritize security, performance, and backward compatibility in all changes.
