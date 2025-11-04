namespace ColumnEncryptor.Interfaces;

public interface IEncryptionService
{
    string Encrypt(string plain); // returns JSON string to store
    string Decrypt(string encryptedJson);
}
