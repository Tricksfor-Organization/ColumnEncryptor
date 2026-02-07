using ColumnEncryptor.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ColumnEncryptor.Providers;

public class VaultKeyProvider : BaseKeyProvider
{
    private readonly VaultOptions _vaultOptions;

    protected override string KeysBasePath => _vaultOptions.KeysPath;
    protected override string ProviderName => "HashiCorp Vault";

    public VaultKeyProvider(
        IVaultClient vaultClient, 
        IOptions<VaultOptions> vaultOptions,
        ILogger<VaultKeyProvider> logger)
        : base(vaultClient, logger, TimeSpan.FromMinutes(5))
    {
        _vaultOptions = vaultOptions?.Value ?? throw new ArgumentNullException(nameof(vaultOptions));
        
        // Initialize keys after fields are set
        RefreshKeysFromVault();
    }
}