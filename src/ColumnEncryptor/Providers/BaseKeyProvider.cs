using ColumnEncryptor.Common;
using ColumnEncryptor.Interfaces;
using Microsoft.Extensions.Logging;

namespace ColumnEncryptor.Providers;

/// <summary>
/// Base class for key providers that use vault-based storage with local caching
/// </summary>
public abstract class BaseKeyProvider : IKeyProvider
{
    protected readonly IVaultClient _vaultClient;
    protected readonly ILogger _logger;
    protected readonly object _lock = new();
    
    // Cache keys locally to avoid frequent vault calls
    protected readonly Dictionary<string, EncryptionKey> _keyCache = new();
    protected string? _primaryKeyId;
    protected DateTime _lastCacheRefresh = DateTime.MinValue;
    protected readonly TimeSpan _cacheExpiry;

    /// <summary>
    /// Gets the base path for storing encryption keys in the vault
    /// </summary>
    protected abstract string KeysBasePath { get; }
    
    /// <summary>
    /// Gets the provider name for logging purposes
    /// </summary>
    protected abstract string ProviderName { get; }

    protected BaseKeyProvider(IVaultClient vaultClient, ILogger logger, TimeSpan cacheExpiry)
    {
        _vaultClient = vaultClient ?? throw new ArgumentNullException(nameof(vaultClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheExpiry = cacheExpiry;
        
        // Don't call RefreshKeysFromVault here - let derived classes do it after their fields are initialized
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
            // Store key in vault
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
            
            _logger.LogInformation("Added encryption key {KeyId} to {Provider}", key.Id, ProviderName);
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
            
            _logger.LogInformation("Promoted key {KeyId} to primary in {Provider}", keyId, ProviderName);
        }
    }

    protected void EnsureKeysAreFresh()
    {
        if (DateTime.UtcNow - _lastCacheRefresh > _cacheExpiry)
        {
            RefreshKeysFromVault();
        }
    }

    protected void RefreshKeysFromVault()
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
                var keyPaths = _vaultClient.ListSecretsAsync(KeysBasePath).GetAwaiter().GetResult();
                
                foreach (var keyPath in keyPaths.Where(p => p != "primary"))
                {
                    var fullKeyPath = $"{KeysBasePath}/{keyPath}";
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
                _logger.LogDebug("Refreshed {Count} keys from {Provider}", _keyCache.Count, ProviderName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh keys from {Provider}", ProviderName);
            throw new InvalidOperationException($"Failed to refresh encryption keys from {ProviderName}. Check connectivity and authentication.", ex);
        }
    }

    protected void UpdatePrimaryKeyInVault()
    {
        var primaryKeyData = new PrimaryKeyData { KeyId = _primaryKeyId };
        var primaryKeyPath = GetPrimaryKeyPath();
        _vaultClient.WriteSecretAsync(primaryKeyPath, primaryKeyData).GetAwaiter().GetResult();
    }

    protected string GetKeyPath(string keyId) => $"{KeysBasePath}/{keyId}";
    protected string GetPrimaryKeyPath() => $"{KeysBasePath}/primary";

    // DTOs for vault storage
    protected sealed class VaultKeyData
    {
        public string Id { get; set; } = string.Empty;
        public string KeyBase64 { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
    }

    protected sealed class PrimaryKeyData
    {
        public string? KeyId { get; set; }
    }
}
