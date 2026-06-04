namespace SpiritsToBottlePOSMigrationUtility.Models;

public sealed record MigrationRequest(
    string SourceDirectory,
    string OutputDirectory,
    ExportOptions Options,
    bool PreviewOnly = false);
