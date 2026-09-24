namespace EsilvaSoft.SlopStudio.Application;

public enum LegacyConnectionCredentialMigrationStatus
{
    Migrated,
    AlreadyMigrated,
    NotEligible,
    ProfileNotFound,
    Conflict,
    OrphanCleaned,
    SecretStoreFailed,
    VerificationFailed
}

/// <summary>A secret-free outcome. FailureCode is set only for a typed store failure.</summary>
public sealed record LegacyConnectionCredentialMigrationResult(
    LegacyConnectionCredentialMigrationStatus Status,
    SecretStoreFailureCode? FailureCode = null);

/// <summary>Explicit, restartable migration of one legacy MongoDB profile.</summary>
public interface ILegacyConnectionCredentialMigration
{
    Task<LegacyConnectionCredentialMigrationResult> MigrateAsync(Guid profileId,
        CancellationToken cancellationToken = default);
}
