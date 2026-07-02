using SpiritsToBottlePOSMigrationUtility.Models;

namespace SpiritsToBottlePOSMigrationUtility.Services;

public static class MigrationCatalog
{
    private static readonly IReadOnlyDictionary<string, string[]> RequiredTablesByExport =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["departments"] = ["CAT.DBF", "TYP.DBF"],
            ["vendors"] = ["VND.DBF"],
            ["customers"] = ["CUS.DBF"],
            ["inventory"] = ["INV.DBF", "STR.DBF", "STK.DBF", "PRC.DBF", "DSC.DBF", "UPC.DBF", "CNT.DBF", "TXC.DBF", "VND.DBF", "CAT.DBF"],
            ["giftcards"] = ["GIFTCARD.DBF"]
        };

    public static IReadOnlyList<string> GetPlannedOutputs(ExportOptions options)
    {
        var outputs = new List<string>();

        if (options.ExportDepartments)
        {
            outputs.Add("1_departments.csv");
        }

        if (options.ExportVendors)
        {
            outputs.Add("2_vendors.csv");
        }

        if (options.ExportCustomers)
        {
            outputs.Add("3_customers.csv");
        }

        if (options.ExportInventory)
        {
            outputs.Add("4_inventory.csv");
            outputs.Add("reference_SalePrices.csv");
            outputs.Add("reference_InactiveItems.csv");
            outputs.Add("reference_UPCModifierLinkAudit.html");
        }

        if (options.ExportGiftCards)
        {
            outputs.Add("5_gift_cards.csv");
        }

        return outputs;
    }

    public static IReadOnlyList<string> GetRequiredTables(ExportOptions options)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (options.ExportDepartments)
        {
            AddTables(tables, RequiredTablesByExport["departments"]);
        }

        if (options.ExportVendors)
        {
            AddTables(tables, RequiredTablesByExport["vendors"]);
        }

        if (options.ExportCustomers)
        {
            AddTables(tables, RequiredTablesByExport["customers"]);
        }

        if (options.ExportInventory)
        {
            AddTables(tables, RequiredTablesByExport["inventory"]);
        }

        if (options.ExportGiftCards)
        {
            AddTables(tables, RequiredTablesByExport["giftcards"]);
        }

        return tables.OrderBy(table => table, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> GetRequiredTablesForExport(string exportKey)
    {
        return RequiredTablesByExport.TryGetValue(exportKey, out var tables)
            ? tables
            : Array.Empty<string>();
    }

    public static IReadOnlyList<string> GetSelectedExports(ExportOptions options)
    {
        var exports = new List<string>();

        if (options.ExportDepartments)
        {
            exports.Add("Departments");
        }

        if (options.ExportVendors)
        {
            exports.Add("Vendors");
        }

        if (options.ExportCustomers)
        {
            exports.Add("Customers");
        }

        if (options.ExportInventory)
        {
            exports.Add("Inventory");
        }

        if (options.ExportGiftCards)
        {
            exports.Add("Gift Cards");
        }

        return exports;
    }

    private static void AddTables(ISet<string> destination, IEnumerable<string> source)
    {
        foreach (var table in source)
        {
            destination.Add(table);
        }
    }
}
