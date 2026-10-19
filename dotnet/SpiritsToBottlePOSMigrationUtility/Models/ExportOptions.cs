namespace SpiritsToBottlePOSMigrationUtility.Models;

public sealed record ExportOptions(
    bool ExportDepartments,
    bool ExportVendors,
    bool ExportCustomers,
    bool ExportInventory,
    bool ExportGiftCards,
    bool IncludeInactiveProducts,
    bool AddQuantityOneIfMissing = true)
{
    public bool HasSelections =>
        ExportDepartments ||
        ExportVendors ||
        ExportCustomers ||
        ExportInventory ||
        ExportGiftCards;
}
