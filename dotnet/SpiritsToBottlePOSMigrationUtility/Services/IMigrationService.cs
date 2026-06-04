using SpiritsToBottlePOSMigrationUtility.Models;

namespace SpiritsToBottlePOSMigrationUtility.Services;

public interface IMigrationService
{
    Task<MigrationResult> RunAsync(
        MigrationRequest request,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
