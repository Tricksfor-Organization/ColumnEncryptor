namespace ColumnEncryptor.Common;

public record EncryptionKey(string Id, byte[] KeyBytes, DateTime CreatedUtc);