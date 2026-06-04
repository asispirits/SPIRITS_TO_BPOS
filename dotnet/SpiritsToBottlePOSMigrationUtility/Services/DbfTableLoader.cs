using System.Data.Common;
using System.Text;
using DbfDataReader;

namespace SpiritsToBottlePOSMigrationUtility.Services;

internal sealed class DbfTableLoader
{
    private readonly DbfDataReaderOptions _options;

    static DbfTableLoader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public DbfTableLoader()
    {
        _options = new DbfDataReaderOptions
        {
            SkipDeletedRecords = true,
            Encoding = Encoding.GetEncoding(1252)
        };
    }

    public List<Dictionary<string, object?>> ReadRows(string path, params string[] columns)
    {
        using var reader = new DbfDataReader.DbfDataReader(path, _options);
        var ordinals = BuildOrdinals(reader, columns);
        var rows = new List<Dictionary<string, object?>>();

        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var column in columns)
            {
                var ordinal = ordinals[column];
                row[column] = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static IReadOnlyDictionary<string, int> BuildOrdinals(DbDataReader reader, IEnumerable<string> requestedColumns)
    {
        var available = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < reader.FieldCount; index++)
        {
            available[reader.GetName(index)] = index;
        }

        var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in requestedColumns)
        {
            if (!available.TryGetValue(column, out var ordinal))
            {
                throw new InvalidOperationException($"The DBF table is missing the expected column '{column}'.");
            }

            ordinals[column] = ordinal;
        }

        return ordinals;
    }
}
