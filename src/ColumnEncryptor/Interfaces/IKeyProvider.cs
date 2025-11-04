using ColumnEncryptor.Common;

namespace ColumnEncryptor.Interfaces;

public interface IKeyProvider
{
    EncryptionKey GetPrimaryKey();
    EncryptionKey? GetKey(string keyId);
    IEnumerable<EncryptionKey> GetAllKeys();
    void AddKey(EncryptionKey key);
    void PromoteKey(string keyId); // make primary
}