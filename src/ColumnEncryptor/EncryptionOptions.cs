namespace ColumnEncryptor;

public class EncryptionOptions
{
    /// <summary>
    /// Key management provider type
    /// </summary>
    public KeyProviderType KeyProvider { get; set; } = KeyProviderType.HashiCorpVault;
    
    /// <summary>
    /// Initial primary key ID (optional)
    /// </summary>
    public string PrimaryKeyId { get; set; } = "";
    
    /// <summary>
    /// HashiCorp Vault configuration (used when KeyProvider is HashiCorpVault)
    /// </summary>
    public VaultOptions? Vault { get; set; }
    
    /// <summary>
    /// Azure Key Vault configuration (used when KeyProvider is AzureKeyVault)
    /// </summary>
    public AzureKeyVaultOptions? AzureKeyVault { get; set; }
}

public class VaultOptions
{
    /// <summary>
    /// Vault server URL (e.g., https://vault.example.com:8200)
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// Authentication method for Vault
    /// </summary>
    public VaultAuthMethod AuthMethod { get; set; } = VaultAuthMethod.Token;
    
    /// <summary>
    /// Authentication token (for Token auth method)
    /// </summary>
    public string? Token { get; set; }
    
    /// <summary>
    /// Role ID (for AppRole auth method)
    /// </summary>
    public string? RoleId { get; set; }
    
    /// <summary>
    /// Secret ID (for AppRole auth method)
    /// </summary>
    public string? SecretId { get; set; }
    
    /// <summary>
    /// Path in Vault where encryption keys are stored
    /// </summary>
    public string KeysPath { get; set; } = "secret/encryption-keys";
    
    /// <summary>
    /// Vault namespace (for Vault Enterprise)
    /// </summary>
    public string? Namespace { get; set; }
    
    /// <summary>
    /// Key cache expiry time in minutes
    /// </summary>
    public int CacheExpiryMinutes { get; set; } = 5;
}

public class AzureKeyVaultOptions
{
    /// <summary>
    /// Azure Key Vault URL (e.g., https://myvault.vault.azure.net/)
    /// </summary>
    public string VaultUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// Authentication method for Azure Key Vault
    /// </summary>
    public AzureAuthMethod AuthMethod { get; set; } = AzureAuthMethod.DefaultAzureCredential;
    
    /// <summary>
    /// Azure AD Tenant ID (for Service Principal auth)
    /// </summary>
    public string? TenantId { get; set; }
    
    /// <summary>
    /// Application (Client) ID (for Service Principal and Managed Identity auth)
    /// </summary>
    public string? ClientId { get; set; }
    
    /// <summary>
    /// Client Secret (for Service Principal auth)
    /// </summary>
    public string? ClientSecret { get; set; }
    
    /// <summary>
    /// Prefix for encryption key secrets in Azure Key Vault
    /// </summary>
    public string KeyPrefix { get; set; } = "encryption-keys";
    
    /// <summary>
    /// Key cache expiry time in minutes
    /// </summary>
    public int CacheExpiryMinutes { get; set; } = 5;
}

public enum KeyProviderType
{
    HashiCorpVault,
    AzureKeyVault
}

public enum VaultAuthMethod
{
    Token,
    AppRole,
    Kubernetes,
    AWS
}

public enum AzureAuthMethod
{
    DefaultAzureCredential,
    ManagedIdentity,
    ServicePrincipal
}
