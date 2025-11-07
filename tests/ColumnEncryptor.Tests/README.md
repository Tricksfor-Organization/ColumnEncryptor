# ColumnEncryptor.Tests

Integration and unit tests for the ColumnEncryptor library.

## 🧪 Test Coverage

This test suite covers:

- **Azure Key Vault Integration** - Authentication, key management, and encryption/decryption
- **HashiCorp Vault Integration** - Token/AppRole auth, key storage, and operations
- **Entity Framework Core Integration** - Automatic encryption/decryption with real database
- **Encryption Service** - AES-GCM operations, key rotation, and error handling

## 🚀 Running Tests

### Prerequisites

- .NET 9.0 SDK
- Docker (for Testcontainers - SQL Server and HashiCorp Vault)
- Azure Key Vault access (for Azure tests - optional)

### Run All Tests

```bash
dotnet test
```

### Run Specific Test Class

```bash
dotnet test --filter "FullyQualifiedName~EntityFrameworkIntegrationTests"
dotnet test --filter "FullyQualifiedName~HashiCorpTests"
dotnet test --filter "FullyQualifiedName~AzureKeyVaultTests"
```

## 🔧 Test Architecture

### Testcontainers Integration

Tests use [Testcontainers](https://testcontainers.com/) for real infrastructure:

- **SQL Server** - For EF Core integration tests
- **HashiCorp Vault** - For Vault provider tests

Containers are automatically started before tests and cleaned up after.

### Test Fixtures

Tests use NUnit's `[OneTimeSetUp]` for expensive container initialization:

```csharp
[OneTimeSetUp]
public async Task Setup()
{
    _vaultContainer = new ContainerBuilder()
        .WithImage("hashicorp/vault:1.15")
        .WithPortBinding(VaultPort, true)
        .Build();
    
    await _vaultContainer.StartAsync();
}
```

## 📋 Test Categories

### Entity Framework Integration Tests

- Property encryption/decryption
- Multiple entity handling
- Change tracking integration
- Database round-trip verification

### Azure Key Vault Tests

- DefaultAzureCredential authentication
- Service Principal authentication
- Managed Identity authentication
- Key storage and retrieval
- Key rotation

### HashiCorp Vault Tests

- Token authentication
- AppRole authentication
- Key management operations
- Namespace support (Enterprise)
- KV v2 secrets engine

## 🔐 Azure Key Vault Testing

To run Azure Key Vault tests, set up environment variables:

```bash
export AZURE_KEYVAULT_URL="https://your-keyvault.vault.azure.net/"
export AZURE_TENANT_ID="your-tenant-id"
export AZURE_CLIENT_ID="your-client-id"
export AZURE_CLIENT_SECRET="your-client-secret"
```

Or use `appsettings.azure.example.json` as a template.

## 🛠️ Development

### Adding New Tests

1. Create test class with `[TestFixture]` attribute
2. Use descriptive test method names (e.g., `ShouldEncryptPropertyMarkedWithEncryptedAttribute`)
3. Use FluentAssertions for readable assertions
4. Clean up resources in `[OneTimeTearDown]`

### Test Naming Convention

```csharp
[Test]
public async Task ShouldEncryptPropertyMarkedWithEncryptedAttribute()
{
    // Arrange
    var entity = new User { Email = "test@example.com" };
    
    // Act
    await _dbContext.Users.AddAsync(entity);
    await _dbContext.SaveChangesAsync();
    
    // Assert
    var saved = await _dbContext.Users.FirstAsync();
    saved.Email.Should().NotStartWith("{"); // Decrypted automatically
}
```

## 📚 Dependencies

- **NUnit** - Test framework
- **FluentAssertions** - Assertion library
- **Testcontainers** - Container orchestration for tests
- **Entity Framework Core** - Database integration
- **Microsoft.Data.SqlClient** - SQL Server connectivity

## 🤝 Contributing

When adding tests:

1. Ensure tests are isolated and can run in any order
2. Use Testcontainers for external dependencies
3. Mock external services when appropriate
4. Add comprehensive assertions
5. Document complex test scenarios

## 📝 License

Licensed under the MIT License. See [LICENSE](https://github.com/Tricksfor-Organization/ColumnEncryptor/blob/main/LICENSE) for details.
