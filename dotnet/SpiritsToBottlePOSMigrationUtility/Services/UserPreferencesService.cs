using System.Text;

namespace SpiritsToBottlePOSMigrationUtility.Services;

internal sealed class UserPreferencesService
{
    private readonly string _preferencesPath;

    public UserPreferencesService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ASI Spirits",
            "SpiritsToBottlePOSMigrationUtility");

        _preferencesPath = Path.Combine(folder, "preferences.txt");
    }

    public string LoadLastOutputDirectory()
    {
        if (!File.Exists(_preferencesPath))
        {
            return string.Empty;
        }

        try
        {
            var path = File.ReadAllText(_preferencesPath, Encoding.UTF8).Trim();
            return Directory.Exists(path) ? path : string.Empty;
        }
        catch (IOException)
        {
            System.Diagnostics.Debug.WriteLine("Could not load the last output folder preference.");
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine("No permission to load the last output folder preference.");
            return string.Empty;
        }
    }

    public void SaveLastOutputDirectory(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_preferencesPath)!);
            File.WriteAllText(_preferencesPath, outputDirectory.Trim(), Encoding.UTF8);
        }
        catch (IOException)
        {
            System.Diagnostics.Debug.WriteLine("Could not save the last output folder preference.");
        }
        catch (UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine("No permission to save the last output folder preference.");
        }
    }
}
