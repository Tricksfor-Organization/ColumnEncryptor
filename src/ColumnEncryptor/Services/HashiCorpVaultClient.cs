using ColumnEncryptor.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ColumnEncryptor.Services;

/// <summary>
/// HashiCorp Vault client implementation
/// </summary>
public sealed class HashiCorpVaultClient : IVaultClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly VaultOptions _options;
    private readonly ILogger<HashiCorpVaultClient> _logger;
    private string? _authToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public HashiCorpVaultClient(
        HttpClient httpClient,
        IOptions<VaultOptions> options,
        ILogger<HashiCorpVaultClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(_options.ServerUrl))
        {
            throw new ArgumentException("Vault ServerUrl must be configured", nameof(options));
        }

        _httpClient.BaseAddress = new Uri(_options.ServerUrl.TrimEnd('/'));
        
        if (!string.IsNullOrWhiteSpace(_options.Namespace))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Vault-Namespace", _options.Namespace);
        }
    }

    public async Task<T?> ReadSecretAsync<T>(string path) where T : class
    {
        await EnsureAuthenticatedAsync();
        
        var apiPath = ConvertToApiPath(path);
        var response = await _httpClient.GetAsync($"/v1/{apiPath}");
        
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        var vaultResponse = JsonSerializer.Deserialize<VaultReadResponse>(content);
        
        if (vaultResponse?.Data == null)
        {
            return null;
        }
        
        var dataJson = JsonSerializer.Serialize(vaultResponse.Data);
        return JsonSerializer.Deserialize<T>(dataJson);
    }

    public async Task WriteSecretAsync<T>(string path, T data) where T : class
    {
        await EnsureAuthenticatedAsync();
        
        var apiPath = ConvertToApiPath(path);
        var payload = new VaultWriteRequest
        {
            Data = data
        };
        
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        var response = await _httpClient.PostAsync($"/v1/{apiPath}", content);
        response.EnsureSuccessStatusCode();
        
        _logger.LogDebug("Successfully wrote secret to path: {Path}", path);
    }

    public async Task<IEnumerable<string>> ListSecretsAsync(string path)
    {
        await EnsureAuthenticatedAsync();
        
        var apiPath = ConvertToApiPath(path, isListOperation: true);
        var response = await _httpClient.GetAsync($"/v1/{apiPath}?list=true");
        
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Enumerable.Empty<string>();
        }
        
        response.EnsureSuccessStatusCode();
        
        var content = await response.Content.ReadAsStringAsync();
        var vaultResponse = JsonSerializer.Deserialize<VaultListResponse>(content);
        
        return vaultResponse?.Data?.Keys ?? Enumerable.Empty<string>();
    }

    public async Task DeleteSecretAsync(string path)
    {
        await EnsureAuthenticatedAsync();
        
        var apiPath = ConvertToApiPath(path);
        var response = await _httpClient.DeleteAsync($"/v1/{apiPath}");
        
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
        
        _logger.LogDebug("Successfully deleted secret at path: {Path}", path);
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (!string.IsNullOrEmpty(_authToken) && DateTime.UtcNow < _tokenExpiry)
        {
            return; // Token is still valid
        }

        await AuthenticateAsync();
    }

    private async Task AuthenticateAsync()
    {
        switch (_options.AuthMethod)
        {
            case VaultAuthMethod.Token:
                await AuthenticateWithTokenAsync();
                break;
            case VaultAuthMethod.AppRole:
                await AuthenticateWithAppRoleAsync();
                break;
            default:
                throw new NotSupportedException($"Authentication method {_options.AuthMethod} is not supported");
        }
        
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);
        _logger.LogDebug("Successfully authenticated with Vault using {Method}", _options.AuthMethod);
    }

    private Task AuthenticateWithTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException("Token must be provided for Token authentication method");
        }

        _authToken = _options.Token;
        _tokenExpiry = DateTime.UtcNow.AddHours(1); // Assume token is valid for 1 hour, adjust as needed
        return Task.CompletedTask;
    }

    private async Task AuthenticateWithAppRoleAsync()
    {
        if (string.IsNullOrWhiteSpace(_options.RoleId) || string.IsNullOrWhiteSpace(_options.SecretId))
        {
            throw new InvalidOperationException("RoleId and SecretId must be provided for AppRole authentication method");
        }

        var loginRequest = new
        {
            role_id = _options.RoleId,
            secret_id = _options.SecretId
        };

        var json = JsonSerializer.Serialize(loginRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/v1/auth/approle/login", content);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var authResponse = JsonSerializer.Deserialize<VaultAuthResponse>(responseContent);

        if (authResponse?.Auth == null)
        {
            throw new InvalidOperationException("Failed to authenticate with Vault: Invalid response");
        }

        _authToken = authResponse.Auth.ClientToken;
        _tokenExpiry = DateTime.UtcNow.AddSeconds(authResponse.Auth.LeaseDuration ?? 3600);
    }

    private static string ConvertToApiPath(string path, bool isListOperation = false)
    {
        // Convert secret/data/path to secret/data/path for KV v2 or secret/path for KV v1
        // For simplicity, assuming KV v2 engine
        if (path.StartsWith("secret/") && !path.Contains("/data/"))
        {
            if (isListOperation)
            {
                return path.Replace("secret/", "secret/metadata/");
            }
            return path.Replace("secret/", "secret/data/");
        }
        
        return path;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    // Vault API response models
    private sealed class VaultReadResponse
    {
        public JsonElement? Data { get; set; }
    }

    private sealed class VaultWriteRequest
    {
        [JsonPropertyName("data")]
        public object? Data { get; set; }
    }

    private sealed class VaultListResponse
    {
        public VaultListData? Data { get; set; }
    }

    private sealed class VaultListData
    {
        public string[]? Keys { get; set; }
    }

    private sealed class VaultAuthResponse
    {
        public VaultAuthData? Auth { get; set; }
    }

    private sealed class VaultAuthData
    {
        [JsonPropertyName("client_token")]
        public string? ClientToken { get; set; }
        
        [JsonPropertyName("lease_duration")]
        public int? LeaseDuration { get; set; }
    }
}