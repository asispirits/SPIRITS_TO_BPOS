using SpiritsToBottlePOSMigrationUtility.Models;
using SpiritsToBottlePOSMigrationUtility.Services;

var arguments = ParseArguments(args);

if (!arguments.TryGetValue("source", out var sourceDirectory) ||
    !arguments.TryGetValue("output", out var outputDirectory))
{
    WriteUsage();
    return 1;
}

var options = new ExportOptions(
    GetBool(arguments, "departments", true),
    GetBool(arguments, "vendors", true),
    GetBool(arguments, "customers", true),
    GetBool(arguments, "inventory", true),
    GetBool(arguments, "giftcards", true),
    GetBool(arguments, "includeinactive", false),
    GetBool(arguments, "addqty1ifmissing", false),
    GetBool(arguments, "usedefaultpricelevel", true),
    arguments.GetValueOrDefault("pricelevel", "1"));

var request = new MigrationRequest(
    sourceDirectory,
    outputDirectory,
    options,
    GetBool(arguments, "preview", false));

var service = new MigrationService();
MigrationResult result;
try
{
    result = await service.RunAsync(request);
}
catch (IOException ex)
{
    WriteFailure("The migration could not read or write one of the required files. Close any open Spirits/KSV tables, CSVs, ZIP files, or audit reports, then try again.", ex);
    return 2;
}
catch (UnauthorizedAccessException ex)
{
    WriteFailure("The migration does not have permission to read or write one of the selected paths. Check the folder permissions or choose a different output folder.", ex);
    return 2;
}
catch (Exception ex)
{
    WriteFailure("The migration stopped unexpectedly.", ex);
    return 2;
}

Console.WriteLine($"Success: {result.IsSuccess}");
Console.WriteLine($"Preview: {result.IsPreview}");

if (!string.IsNullOrWhiteSpace(result.PlannedOutputDirectory))
{
    Console.WriteLine($"{(result.IsPreview ? "Planned" : "Output")} folder: {result.PlannedOutputDirectory}");
}

if (!string.IsNullOrWhiteSpace(result.ZipFilePath))
{
    Console.WriteLine($"ZIP archive: {result.ZipFilePath}");
}

Console.WriteLine();
Console.WriteLine(result.Summary);

if (result.Issues.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Issues:");
    foreach (var issue in result.Issues)
    {
        Console.WriteLine($"- {issue}");
    }
}

if (result.PlannedOutputs.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine(result.IsPreview ? "Planned outputs:" : "Output files:");
    foreach (var output in result.IsPreview ? result.PlannedOutputs : result.CreatedFiles)
    {
        Console.WriteLine($"- {output}");
    }
}

return result.IsSuccess ? 0 : 2;

static Dictionary<string, string> ParseArguments(string[] args)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (var index = 0; index < args.Length; index++)
    {
        var current = args[index];
        if (!current.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var key = current[2..];
        var value = "true";

        if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = args[++index];
        }

        values[key] = value;
    }

    return values;
}

static bool GetBool(IReadOnlyDictionary<string, string> arguments, string key, bool fallback)
{
    if (!arguments.TryGetValue(key, out var rawValue))
    {
        return fallback;
    }

    return !rawValue.Equals("false", StringComparison.OrdinalIgnoreCase) &&
           !rawValue.Equals("0", StringComparison.OrdinalIgnoreCase) &&
           !rawValue.Equals("no", StringComparison.OrdinalIgnoreCase);
}

static void WriteUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine(@"  dotnet run --project .\dotnet\SpiritsToBottlePOSMigrationUtility.Runner -- --source ""D:\path\to\Data"" --output ""D:\path\to\Output"" [--giftcards false] [--includeinactive true] [--addqty1ifmissing false] [--pricelevel 1] [--preview true]");
}

static void WriteFailure(string message, Exception ex)
{
    Console.WriteLine("Success: False");
    Console.WriteLine("Preview: False");
    Console.WriteLine();
    Console.WriteLine(message);
    Console.WriteLine();
    Console.WriteLine(ex.Message);
}
