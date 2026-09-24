using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Persists a recovery marker before touching the OS store and commits the redacted profile only after readback.</summary>
public sealed class LegacyConnectionCredentialMigration(
    LiteDbConnectionProfileRepository profiles,
    ISecretStore secrets) : ILegacyConnectionCredentialMigration
{
    public async Task<LegacyConnectionCredentialMigrationResult> MigrateAsync(Guid profileId,
        CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty) throw new ArgumentException("Perfil inválido.", nameof(profileId));
        var candidate = await profiles.PrepareCredentialMigrationAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (candidate.Status != CredentialMigrationPreparationStatus.Ready)
        {
            if (candidate.Status != CredentialMigrationPreparationStatus.Orphaned)
                return new(candidate.Status switch
                {
                    CredentialMigrationPreparationStatus.AlreadyMigrated => LegacyConnectionCredentialMigrationStatus.AlreadyMigrated,
                    CredentialMigrationPreparationStatus.NotEligible => LegacyConnectionCredentialMigrationStatus.NotEligible,
                    _ => LegacyConnectionCredentialMigrationStatus.ProfileNotFound
                });

            var cleanup = await secrets.DeleteAsync(candidate.Reference!, cancellationToken).ConfigureAwait(false);
            if (!cleanup.IsSuccess && cleanup.Failure!.Code != SecretStoreFailureCode.NotFound)
                return new(LegacyConnectionCredentialMigrationStatus.SecretStoreFailed, cleanup.Failure.Code);
            var removed = await profiles.ClearOrphanedCredentialMigrationAsync(profileId, candidate.Reference!, cancellationToken)
                .ConfigureAwait(false);
            return new(removed ? LegacyConnectionCredentialMigrationStatus.OrphanCleaned
                : LegacyConnectionCredentialMigrationStatus.Conflict);
        }

        // A retry uses the same persisted pending reference. Set is idempotent for the captured URI.
        var set = await secrets.SetAsync(candidate.Reference!, candidate.OriginalUri!, cancellationToken).ConfigureAwait(false);
        if (!set.IsSuccess)
            return new(LegacyConnectionCredentialMigrationStatus.SecretStoreFailed, set.Failure!.Code);
        var readback = await secrets.GetAsync(candidate.Reference!, cancellationToken).ConfigureAwait(false);
        if (!readback.IsSuccess)
            return new(LegacyConnectionCredentialMigrationStatus.SecretStoreFailed, readback.Failure!.Code);
        if (!string.Equals(readback.Value, candidate.OriginalUri, StringComparison.Ordinal))
            return new(LegacyConnectionCredentialMigrationStatus.VerificationFailed);

        var committed = await profiles.CommitCredentialMigrationAsync(candidate, cancellationToken).ConfigureAwait(false);
        return new(committed ? LegacyConnectionCredentialMigrationStatus.Migrated
            : LegacyConnectionCredentialMigrationStatus.Conflict);
    }
}
