using ColumnEncryptor.Common;
using ColumnEncryptor.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ColumnEncryptor.Providers;

public class VaultKeyProvider : IKeyProvider
{
    private readonly IVaultClient _vaultClient;
    private readonly VaultOptions _vaultOptions;
    private readonly ILogger<VaultKeyProvider> _logger;
    private readonly object _lock = new();
    
    // Cache keys locally to avoid frequent Vault calls
    private readonly Dictionary<string, EncryptionKey> _keyCache = new();
    private string? _primaryKeyId;
    private DateTime _lastCacheRefresh = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5); // configurable

    public VaultKeyProvider(
        IVaultClient vaultClient, 
        IOptions<VaultOptions> vaultOptions,
        ILogger<VaultKeyProvider> logger)
    {
        _vaultClient = vaultClient ?? throw new ArgumentNullException(nameof(vaultClient));
        _vaultOptions = vaultOptions.Value ?? throw new ArgumentNullException(nameof(vaultOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        RefreshKeysFromVault();
    }

    public EncryptionKey GetPrimaryKey()
    {
        EnsureKeysAreFresh();
        
        if (string.IsNullOrEmpty(_primaryKeyId))
        {
            throw new InvalidOperationException("No primary key is configured");
        }

        return GetKey(_primaryKeyId) ?? throw new InvalidOperationException($"Primary key '{_primaryKeyId}' not found");
    }

    public EncryptionKey? GetKey(string keyId)
    {
        EnsureKeysAreFresh();
        
        lock (_lock)
        {
            return _keyCache.TryGetValue(keyId, out var key) ? key : null;
        }
    }

    public IEnumerable<EncryptionKey> GetAllKeys()
    {
        EnsureKeysAreFresh();
        
        lock (_lock)
        {
            return _keyCache.Values.ToList();
        }
    }

    public void AddKey(EncryptionKey key)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));
        
        lock (_lock)
        {
            // Store key in Vault
            var keyData = new VaultKeyData
            {
                Id = key.Id,
                KeyBase64 = Convert.ToBase64String(key.KeyBytes),
                CreatedUtc = key.CreatedUtc
            };

            var keyPath = GetKeyPath(key.Id);
            _vaultClient.WriteSecretAsync(keyPath, keyData).GetAwaiter().GetResult();
            
            // Update cache
            _keyCache[key.Id] = key;
            
            // If no primary key is set, make this the primary
            if (string.IsNullOrEmpty(_primaryKeyId))
            {
                _primaryKeyId = key.Id;
                UpdatePrimaryKeyInVault();
            }
            
            _logger.LogInformation("Added encryption key {KeyId} to Vault", key.Id);
        }
    }

    public void PromoteKey(string keyId)
    {
        if (string.IsNullOrEmpty(keyId)) throw new ArgumentException("Key ID cannot be null or empty", nameof(keyId));
        
        EnsureKeysAreFresh();
        
        lock (_lock)
        {
            if (!_keyCache.ContainsKey(keyId))
            {
                throw new InvalidOperationException($"Key '{keyId}' not found");
            }
            
            _primaryKeyId = keyId;
            UpdatePrimaryKeyInVault();
            
            _logger.LogInformation("Promoted key {KeyId} to primary", keyId);
        }
    }

    private void EnsureKeysAreFresh()
    {
        if (DateTime.UtcNow - _lastCacheRefresh > _cacheExpiry)
        {
            RefreshKeysFromVault();
        }
    }

    private void RefreshKeysFromVault()
    {
        try
        {
            lock (_lock)
            {
                _keyCache.Clear();
                
                // Load primary key ID
                var primaryKeyPath = GetPrimaryKeyPath();
                var primaryKeyData = _vaultClient.ReadSecretAsync<PrimaryKeyData>(primaryKeyPath).GetAwaiter().GetResult();
                _primaryKeyId = primaryKeyData?.KeyId;
                
                // Load all keys
                var keyPaths = _vaultClient.ListSecretsAsync(_vaultOptions.KeysPath).GetAwaiter().GetResult();
                
                foreach (var keyPath in keyPaths.Where(p => p != "primary"))
                {
                    var fullKeyPath = $"{_vaultOptions.KeysPath}/{keyPath}";
                    var keyData = _vaultClient.ReadSecretAsync<VaultKeyData>(fullKeyPath).GetAwaiter().GetResult();
                    
                    if (keyData != null)
                    {
                        var key = new EncryptionKey(
                            keyData.Id,
                            Convert.FromBase64String(keyData.KeyBase64),
                            keyData.CreatedUtc
                        );
                        _keyCache[keyData.Id] = key;
                    }
                }
                
                _lastCacheRefresh = DateTime.UtcNow;
                _logger.LogDebug("Refreshed {Count} keys from Vault", _keyCache.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh keys from Vault");
            // Re-throw with additional context
            throw new InvalidOperationException("Failed to refresh encryption keys from Vault. Check Vault connectivity and authentication.", ex);
        }
    }

    private void UpdatePrimaryKeyInVault()
    {
        var primaryKeyData = new PrimaryKeyData { KeyId = _primaryKeyId };
        var primaryKeyPath = GetPrimaryKeyPath();
        _vaultClient.WriteSecretAsync(primaryKeyPath, primaryKeyData).GetAwaiter().GetResult();
    }

    private string GetKeyPath(string keyId) => $"{_vaultOptions.KeysPath}/{keyId}";
    private string GetPrimaryKeyPath() => $"{_vaultOptions.KeysPath}/primary";

    // DTOs for Vault storage
    private sealed class VaultKeyData
    {
        public string Id { get; set; } = string.Empty;
        public string KeyBase64 { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
    }

    private sealed class PrimaryKeyData
    {
        public string? KeyId { get; set; }
    }
}