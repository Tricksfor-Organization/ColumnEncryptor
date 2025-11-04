using ColumnEncryptor.Attributes;
using ColumnEncryptor.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Reflection;

namespace ColumnEncryptor.Extensions;

/// <summary>
/// Extension methods for Entity Framework Core integration with column encryption
/// </summary>
public static class DbContextExtensions
{
    /// <summary>
    /// Configures automatic encryption/decryption for properties marked with EncryptedAttribute
    /// Call this in your DbContext's OnModelCreating method
    /// </summary>
    /// <param name="modelBuilder">The model builder</param>
    /// <param name="encryptionService">The encryption service</param>
    public static void UseColumnEncryption(this ModelBuilder modelBuilder, IEncryptionService encryptionService)
    {
        if (modelBuilder == null) throw new ArgumentNullException(nameof(modelBuilder));
        if (encryptionService == null) throw new ArgumentNullException(nameof(encryptionService));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            ConfigureEncryptedProperties(modelBuilder, entityType);
        }
    }

    /// <summary>
    /// Processes encryption/decryption for entities before saving changes
    /// Call this in your DbContext's SaveChanges/SaveChangesAsync methods
    /// </summary>
    /// <param name="context">The DbContext</param>
    /// <param name="encryptionService">The encryption service</param>
    public static void ProcessEncryption(this DbContext context, IEncryptionService encryptionService)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (encryptionService == null) throw new ArgumentNullException(nameof(encryptionService));

        var encryptedEntities = context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .Where(e => HasEncryptedProperties(e.Entity.GetType()));

        foreach (var entity in encryptedEntities)
        {
            EncryptEntityProperties(entity, encryptionService);
        }
    }

    /// <summary>
    /// Processes decryption for entities after loading from database
    /// Call this after loading entities from the database
    /// </summary>
    /// <param name="context">The DbContext</param>
    /// <param name="encryptionService">The encryption service</param>
    public static void ProcessDecryption(this DbContext context, IEncryptionService encryptionService)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (encryptionService == null) throw new ArgumentNullException(nameof(encryptionService));

        var loadedEntities = context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Unchanged || e.State == EntityState.Modified)
            .Where(e => HasEncryptedProperties(e.Entity.GetType()));

        foreach (var entity in loadedEntities)
        {
            DecryptEntityProperties(entity, encryptionService);
        }
    }

    private static void ConfigureEncryptedProperties(ModelBuilder modelBuilder, IMutableEntityType entityType)
    {
        // Note: For automatic encryption/decryption, use the ProcessEncryption and ProcessDecryption methods
        // Value converters in EF Core require compile-time constants, so we handle encryption at runtime instead
        var encryptedProperties = entityType.ClrType
            .GetProperties()
            .Where(p => p.GetCustomAttribute<EncryptedAttribute>() != null && p.PropertyType == typeof(string));

        foreach (var property in encryptedProperties)
        {
            var entityTypeBuilder = modelBuilder.Entity(entityType.ClrType);
            
            // Configure the property metadata for identification during save/load
            entityTypeBuilder
                .Property(property.Name)
                .HasAnnotation("IsEncrypted", true);
        }
    }

    private static bool HasEncryptedProperties(Type entityType)
    {
        return entityType.GetProperties()
            .Any(p => p.GetCustomAttribute<EncryptedAttribute>() != null);
    }

    private static void EncryptEntityProperties(EntityEntry entity, IEncryptionService encryptionService)
    {
        var encryptedProperties = entity.Entity.GetType()
            .GetProperties()
            .Where(p => p.GetCustomAttribute<EncryptedAttribute>() != null && p.PropertyType == typeof(string));

        foreach (var property in encryptedProperties)
        {
            var currentValue = property.GetValue(entity.Entity) as string;
            if (!string.IsNullOrEmpty(currentValue) && !IsAlreadyEncrypted(currentValue))
            {
                var encryptedValue = encryptionService.Encrypt(currentValue);
                property.SetValue(entity.Entity, encryptedValue);
            }
        }
    }

    private static void DecryptEntityProperties(EntityEntry entity, IEncryptionService encryptionService)
    {
        var encryptedProperties = entity.Entity.GetType()
            .GetProperties()
            .Where(p => p.GetCustomAttribute<EncryptedAttribute>() != null && p.PropertyType == typeof(string));

        foreach (var property in encryptedProperties)
        {
            var currentValue = property.GetValue(entity.Entity) as string;
            if (!string.IsNullOrEmpty(currentValue) && IsAlreadyEncrypted(currentValue))
            {
                try
                {
                    var decryptedValue = encryptionService.Decrypt(currentValue);
                    property.SetValue(entity.Entity, decryptedValue);
                }
                catch (Exception)
                {
                    // If decryption fails, leave the value as is
                    // This could happen with legacy data or during migration
                }
            }
        }
    }

    private static bool IsAlreadyEncrypted(string value)
    {
        // Check if the value looks like JSON (encrypted payload)
        return value.TrimStart().StartsWith('{') && value.TrimEnd().EndsWith('}');
    }
}