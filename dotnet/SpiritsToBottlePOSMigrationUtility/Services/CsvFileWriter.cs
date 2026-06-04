using System.Text;

namespace SpiritsToBottlePOSMigrationUtility.Services;

internal sealed class CsvFileWriter
{
    private readonly Encoding _encoding;

    static CsvFileWriter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public CsvFileWriter()
    {
        _encoding = Encoding.GetEncoding(1252);
    }

    public async Task WriteAsync(string path, IEnumerable<IReadOnlyList<string>> rows, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, _encoding);

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var csvLine = string.Join(",", row.Select(EscapeCsvValue));
            await writer.WriteLineAsync(csvLine);
        }
    }

    private static string EscapeCsvValue(string value)
    {
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
