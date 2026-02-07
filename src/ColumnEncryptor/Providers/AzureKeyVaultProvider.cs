using ColumnEncryptor.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ColumnEncryptor.Providers;

public class AzureKeyVaultProvider : BaseKeyProvider
{
    private readonly AzureKeyVaultOptions _options;

    protected override string KeysBasePath => _options.KeyPrefix;
    protected override string ProviderName => "Azure Key Vault";

    public AzureKeyVaultProvider(
        IVaultClient vaultClient, 
        IOptions<AzureKeyVaultOptions> options,
        ILogger<AzureKeyVaultProvider> logger)
        : base(vaultClient, logger, TimeSpan.FromMinutes(options?.Value?.CacheExpiryMinutes ?? 5))
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        
        // Initialize keys after fields are set
        RefreshKeysFromVault();
    }
}