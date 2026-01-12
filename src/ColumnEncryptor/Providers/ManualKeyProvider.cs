using ColumnEncryptor.Common;
using ColumnEncryptor.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ColumnEncryptor.Providers;

/// <summary>
/// Key provider that uses manually configured keys from application settings
/// This provider does not require external vault infrastructure
/// </summary>
public class ManualKeyProvider : IKeyProvider
{
    private readonly ManualKeyProviderOptions _options;
    private readonly ILogger<ManualKeyProvider> _logger;
    private readonly object _lock = new();
    
    // In-memory key storage
    private readonly Dictionary<string, EncryptionKey> _keyCache = new();
    private string? _primaryKeyId;

    public ManualKeyProvider(
        IOptions<ManualKeyProviderOptions> options,
        ILogger<ManualKeyProvider> logger)
    {
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        LoadKeysFromConfiguration();
    }

    public EncryptionKey GetPrimaryKey()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_primaryKeyId))
            {
                throw new InvalidOperationException("No primary key is configured");
            }

            return GetKey(_primaryKeyId) ?? throw new InvalidOperationException($"Primary key '{_primaryKeyId}' not found");
        }
    }

    public EncryptionKey? GetKey(string keyId)
    {
        lock (_lock)
        {
            return _keyCache.TryGetValue(keyId, out var key) ? key : null;
        }
    }

    public IEnumerable<EncryptionKey> GetAllKeys()
    {
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
            _keyCache[key.Id] = key;
            
            // If no primary key is set, make this the primary
            if (string.IsNullOrEmpty(_primaryKeyId))
            {
                _primaryKeyId = key.Id;
            }
            
            _logger.LogInformation("Added encryption key {KeyId} to manual key provider", key.Id);
        }
    }

    public void PromoteKey(string keyId)
    {
        if (string.IsNullOrEmpty(keyId)) throw new ArgumentException("Key ID cannot be null or empty", nameof(keyId));
        
        lock (_lock)
        {
            if (!_keyCache.ContainsKey(keyId))
            {
                throw new InvalidOperationException($"Key '{keyId}' not found");
            }
            
            _primaryKeyId = keyId;
            
            _logger.LogInformation("Promoted key {KeyId} to primary in manual key provider", keyId);
        }
    }

    private void LoadKeysFromConfiguration()
    {
        lock (_lock)
        {
            _keyCache.Clear();
            
            if (_options.Keys == null || _options.Keys.Count == 0)
            {
                _logger.LogWarning("No encryption keys configured in manual key provider");
                return;
            }
            
            foreach (var configKey in _options.Keys)
            {
                if (string.IsNullOrEmpty(configKey.Id))
                {
                    _logger.LogWarning("Skipping key with empty ID in configuration");
                    continue;
                }
                
                if (string.IsNullOrEmpty(configKey.KeyBase64))
                {
                    _logger.LogWarning("Skipping key {KeyId} with empty KeyBase64 in configuration", configKey.Id);
                    continue;
                }
                
                try
                {
                    var keyBytes = Convert.FromBase64String(configKey.KeyBase64);
                    
                    // Validate key length (must be 32 bytes for AES-256)
                    if (keyBytes.Length != 32)
                    {
                        _logger.LogError("Key {KeyId} has invalid length {Length}. Expected 32 bytes for AES-256", 
                            configKey.Id, keyBytes.Length);
                        throw new InvalidOperationException(
                            $"Key '{configKey.Id}' has invalid length {keyBytes.Length}. Expected 32 bytes for AES-256");
                    }
                    
                    var key = new EncryptionKey(
                        configKey.Id,
                        keyBytes,
                        configKey.CreatedUtc ?? DateTime.UtcNow
                    );
                    
                    _keyCache[configKey.Id] = key;
                    _logger.LogDebug("Loaded encryption key {KeyId} from configuration", configKey.Id);
                }
                catch (FormatException ex)
                {
                    _logger.LogError(ex, "Failed to decode Base64 key {KeyId}", configKey.Id);
                    throw new InvalidOperationException(
                        $"Key '{configKey.Id}' has invalid Base64 encoding", ex);
                }
            }
            
            // Set primary key
            if (!string.IsNullOrEmpty(_options.PrimaryKeyId))
            {
                if (!_keyCache.ContainsKey(_options.PrimaryKeyId))
                {
                    throw new InvalidOperationException(
                        $"Primary key '{_options.PrimaryKeyId}' not found in configured keys");
                }
                _primaryKeyId = _options.PrimaryKeyId;
            }
            else if (_keyCache.Count > 0)
            {
                // If no primary key specified, use the first key
                _primaryKeyId = _keyCache.Keys.First();
                _logger.LogInformation("No primary key specified, using first key: {KeyId}", _primaryKeyId);
            }
            
            _logger.LogInformation("Loaded {Count} encryption keys from manual configuration, primary key: {PrimaryKeyId}", 
                _keyCache.Count, _primaryKeyId);
        }
    }
}
