namespace SpiritsToBottlePOSMigrationUtility.Services;

internal sealed class KsvDataDirectoryLocator
{
    public const string ExportDirectory = @"C:\BPOS\_EXPORT";

    public string FindActiveDataDirectory()
    {
        return DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Network))
            .Select(drive => Path.Combine(drive.RootDirectory.FullName, "KSV", "DATA"))
            .Where(Directory.Exists)
            .Select(path => new { Path = path, LastActivityUtc = GetLastActivityUtc(path) })
            .OrderByDescending(candidate => candidate.LastActivityUtc)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Path)
            .FirstOrDefault() ?? string.Empty;
    }

    public string EnsureExportDirectory()
    {
        Directory.CreateDirectory(ExportDirectory);
        return ExportDirectory;
    }

    private static DateTime GetLastActivityUtc(string dataDirectory)
    {
        try
        {
            return Directory.EnumerateFiles(dataDirectory)
                .Select(file => new[] { File.GetLastAccessTimeUtc(file), File.GetLastWriteTimeUtc(file) }.Max())
                .DefaultIfEmpty(Directory.GetLastWriteTimeUtc(dataDirectory))
                .Max();
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }
}
