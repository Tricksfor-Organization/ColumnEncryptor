namespace ColumnEncryptor.Interfaces;

/// <summary>
/// Interface for interacting with a Vault service (e.g., HashiCorp Vault, Azure Key Vault)
/// </summary>
public interface IVaultClient
{
    /// <summary>
    /// Reads a secret from the vault
    /// </summary>
    /// <typeparam name="T">Type to deserialize the secret data to</typeparam>
    /// <param name="path">Path to the secret in the vault</param>
    /// <returns>Deserialized secret data or null if not found</returns>
    Task<T?> ReadSecretAsync<T>(string path) where T : class;

    /// <summary>
    /// Writes a secret to the vault
    /// </summary>
    /// <typeparam name="T">Type of the secret data</typeparam>
    /// <param name="path">Path to store the secret in the vault</param>
    /// <param name="data">Data to store</param>
    Task WriteSecretAsync<T>(string path, T data) where T : class;

    /// <summary>
    /// Lists all secrets at the given path
    /// </summary>
    /// <param name="path">Path to list secrets from</param>
    /// <returns>List of secret names/paths</returns>
    Task<IEnumerable<string>> ListSecretsAsync(string path);

    /// <summary>
    /// Deletes a secret from the vault
    /// </summary>
    /// <param name="path">Path to the secret to delete</param>
    Task DeleteSecretAsync(string path);
}