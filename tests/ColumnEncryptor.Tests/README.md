# Tricksfor.ColumnEncryptor.Tests

Integration and unit tests for Tricksfor.ColumnEncryptor library.

## Test Coverage

- Azure Key Vault integration (authentication, key management)
- HashiCorp Vault integration (Token/AppRole auth)
- Entity Framework Core integration (automatic encryption/decryption)
- AES-GCM encryption service (operations, key rotation)

## Running Tests

### Prerequisites

- .NET 9.0 SDK
- Docker (for Testcontainers)
- Azure Key Vault access (optional, for Azure tests)

### Commands

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~EntityFrameworkIntegrationTests"
dotnet test --filter "FullyQualifiedName~HashiCorpTests"
dotnet test --filter "FullyQualifiedName~AzureKeyVaultTests"
```

## Test Architecture

Tests use [Testcontainers](https://testcontainers.com/) for real infrastructure (SQL Server, HashiCorp Vault). Containers are automatically managed.

## Azure Key Vault Testing

Set environment variables or use `appsettings.azure.example.json`:

```bash
export AZURE_KEYVAULT_URL="https://your-keyvault.vault.azure.net/"
export AZURE_TENANT_ID="your-tenant-id"
export AZURE_CLIENT_ID="your-client-id"
export AZURE_CLIENT_SECRET="your-client-secret"
```

## License

MIT License - see [LICENSE](https://github.com/Tricksfor-Organization/ColumnEncryptor/blob/main/LICENSE)
