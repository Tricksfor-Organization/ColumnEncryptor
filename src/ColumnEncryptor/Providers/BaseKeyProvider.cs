using ColumnEncryptor.Common;
using ColumnEncryptor.Interfaces;
using Microsoft.Extensions.Logging;

namespace ColumnEncryptor.Providers;

/// <summary>
/// Base class for key providers that use vault-based storage with local caching
/// </summary>
public abstract class BaseKeyProvider(IVaultClient vaultClient, ILogger logger, TimeSpan cacheExpiry) : IKeyProvider
{
    protected readonly IVaultClient _vaultClient = vaultClient ?? throw new ArgumentNullException(nameof(vaultClient));
    protected readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    protected readonly object _lock = new();
    
    // Cache keys locally to avoid frequent vault calls
    protected readonly Dictionary<string, EncryptionKey> _keyCache = new();
    protected string? _primaryKeyId;
    protected DateTime _lastCacheRefresh = DateTime.MinValue;
    protected readonly TimeSpan _cacheExpiry = cacheExpiry;
    private bool _isRefreshing = false;

    /// <summary>
    /// Gets the base path for storing encryption keys in the vault
    /// </summary>
    protected abstract string KeysBasePath { get; }
    
    /// <summary>
    /// Gets the provider name for logging purposes
    /// </summary>
    protected abstract string ProviderName { get; }

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
        
        // Prepare data outside the lock
        var keyData = new VaultKeyData
        {
            Id = key.Id,
            KeyBase64 = Convert.ToBase64String(key.KeyBytes),
            CreatedUtc = key.CreatedUtc
        };

        var keyPath = GetKeyPath(key.Id);
        
        // Perform vault write outside the lock to avoid blocking readers
        _vaultClient.WriteSecretAsync(keyPath, keyData).GetAwaiter().GetResult();
        
        // Only lock for in-memory cache updates
        bool shouldPromoteToPrimary;
        lock (_lock)
        {
            _keyCache[key.Id] = key;
            shouldPromoteToPrimary = string.IsNullOrEmpty(_primaryKeyId);
            
            if (shouldPromoteToPrimary)
            {
                _primaryKeyId = key.Id;
            }
        }
        
        // Update primary key in vault outside the lock
        if (shouldPromoteToPrimary)
        {
            UpdatePrimaryKeyInVault();
        }
        
        _logger.LogInformation("Added encryption key {KeyId} to {Provider}", key.Id, ProviderName);
    }

    public void PromoteKey(string keyId)
    {
        if (string.IsNullOrEmpty(keyId)) throw new ArgumentException("Key ID cannot be null or empty", nameof(keyId));
        
        EnsureKeysAreFresh();
        
        // Validate key exists and update in-memory state under lock
        lock (_lock)
        {
            if (!_keyCache.ContainsKey(keyId))
            {
                throw new InvalidOperationException($"Key '{keyId}' not found");
            }
            
            _primaryKeyId = keyId;
        }
        
        // Update vault outside the lock
        UpdatePrimaryKeyInVault();
        
        _logger.LogInformation("Promoted key {KeyId} to primary in {Provider}", keyId, ProviderName);
    }

    protected void EnsureKeysAreFresh()
    {
        // Fast path: if cache is fresh, return immediately without locking
        if (DateTime.UtcNow - _lastCacheRefresh <= _cacheExpiry)
            return;
        
        // Cache is potentially stale, use double-check locking to prevent redundant refreshes
        bool shouldRefresh = false;
        lock (_lock)
        {
            // Double-check after acquiring lock - another thread may have already refreshed
            if (DateTime.UtcNow - _lastCacheRefresh > _cacheExpiry && !_isRefreshing)
            {
                _isRefreshing = true;
                shouldRefresh = true;
            }
        }
        
        // Perform refresh outside lock if we won the race
        if (shouldRefresh)
        {
            try
            {
                RefreshKeysFromVault();
            }
            finally
            {
                // Always clear the flag, even if refresh throws
                lock (_lock)
                {
                    _isRefreshing = false;
                }
            }
        }
    }

    protected void RefreshKeysFromVault()
    {
        try
        {
            // Perform all vault reads outside the lock
            var primaryKeyPath = GetPrimaryKeyPath();
            var primaryKeyData = _vaultClient.ReadSecretAsync<PrimaryKeyData>(primaryKeyPath).GetAwaiter().GetResult();
            var newPrimaryKeyId = primaryKeyData?.KeyId;
            
            var keyPaths = _vaultClient.ListSecretsAsync(KeysBasePath).GetAwaiter().GetResult();
            var loadedKeys = new Dictionary<string, EncryptionKey>();
            
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
                    loadedKeys[keyData.Id] = key;
                }
            }
            
            // Only lock to update in-memory cache
            lock (_lock)
            {
                _keyCache.Clear();
                foreach (var kvp in loadedKeys)
                {
                    _keyCache[kvp.Key] = kvp.Value;
                }
                _primaryKeyId = newPrimaryKeyId;
                _lastCacheRefresh = DateTime.UtcNow;
            }
            
            _logger.LogDebug("Refreshed {Count} keys from {Provider}", loadedKeys.Count, ProviderName);
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
