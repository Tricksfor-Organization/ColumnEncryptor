using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ColumnEncryptor.Interfaces;

namespace ColumnEncryptor.Services;

public class AesGcmEncryptionService(IKeyProvider keyProvider) : IEncryptionService
{
    private readonly IKeyProvider _keyProvider = keyProvider;

    public string Encrypt(string plain)
    {
        var key = _keyProvider.GetPrimaryKey();
        var nonce = RandomNumberGenerator.GetBytes(12); // AES-GCM 12 bytes
        var plaintext = Encoding.UTF8.GetBytes(plain);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aesgcm = new AesGcm(key.KeyBytes, 16); // 16 byte tag
        aesgcm.Encrypt(nonce, plaintext, ciphertext, tag);

        var payload = new EncryptedPayload
        {
            Version = 1,
            KeyId = key.Id,
            Nonce = Convert.ToBase64String(nonce),
            CipherText = Convert.ToBase64String(ciphertext),
            Tag = Convert.ToBase64String(tag)
        };

        return JsonSerializer.Serialize(payload);
    }

    public string Decrypt(string encryptedJson)
    {
        var payload = JsonSerializer.Deserialize<EncryptedPayload>(encryptedJson)!;
        if (payload.Version != 1) throw new NotSupportedException("Unsupported encryption version");
        var key = _keyProvider.GetKey(payload.KeyId) ?? throw new InvalidOperationException("Key not found");
        var nonce = Convert.FromBase64String(payload.Nonce);
        var ciphertext = Convert.FromBase64String(payload.CipherText);
        var tag = Convert.FromBase64String(payload.Tag);
        var plaintext = new byte[ciphertext.Length];
        using var aesgcm = new AesGcm(key.KeyBytes, 16); // 16 byte tag
        aesgcm.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private class EncryptedPayload
    {
        public int Version { get; set; }
        public string KeyId { get; set; } = string.Empty;
        public string Nonce { get; set; } = string.Empty;
        public string CipherText { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
    }
}