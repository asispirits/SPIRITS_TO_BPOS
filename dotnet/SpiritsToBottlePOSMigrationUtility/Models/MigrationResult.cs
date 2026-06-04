namespace SpiritsToBottlePOSMigrationUtility.Models;

public sealed class MigrationResult
{
    public bool IsSuccess { get; init; }
    public bool IsPreview { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string PlannedOutputDirectory { get; init; } = string.Empty;
    public string ZipFilePath { get; init; } = string.Empty;
    public IReadOnlyList<string> PlannedOutputs { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CreatedFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
}
