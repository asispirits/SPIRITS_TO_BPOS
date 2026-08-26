using System.Globalization;
using System.IO.Compression;
using System.Text;
using SpiritsToBottlePOSMigrationUtility.Models;

namespace SpiritsToBottlePOSMigrationUtility.Services;

public sealed class MigrationService : IMigrationService
{
    private readonly DbfTableLoader _dbfTableLoader = new();
    private readonly CsvFileWriter _csvFileWriter = new();
    private const string UpcModifierLinkAuditFileName = "reference_UPCModifierLinkAudit.html";

    private static readonly string[] InventoryHeader = new[]
    {
        "ImportType",
        "code",
        "CodeToQTY",
        "LinkedQTY",
        "sku",
        "name",
        "cost",
        "lastcost",
        "price",
        "minprice",
        "DONOTDISCOUNT",
        "qty",
        "unitspercase",
        "POINTSMULTIPLIER",
        "taxname",
        "taxrate",
        "categoryname",
        "suppliername",
        "vendoritemno",
        "Unit_Size",
        "Unit_Type",
        "ModifiersQty",
        "ModifiersCost",
        "ModifiersLatestCost",
        "ModifiersPrice",
        "notes",
        "bottledeposit"
    };

    private static readonly string[] UpcModifierLinkAuditHeader = new[]
    {
        "SKU",
        "UPC",
        "UPC_LEVEL",
        "PRC_LEVEL",
        "ISSUE"
    };

    public async Task<MigrationResult> RunAsync(
        MigrationRequest request,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new MigrationProgress(10, "Validating request..."));
        cancellationToken.ThrowIfCancellationRequested();

        var issues = ValidateRequest(request).ToList();
        var plannedOutputs = MigrationCatalog.GetPlannedOutputs(request.Options);

        if (issues.Count > 0)
        {
            return new MigrationResult
            {
                IsSuccess = false,
                IsPreview = true,
                PlannedOutputs = plannedOutputs,
                Issues = issues,
                Summary = BuildIssueSummary("The request needs a few fixes before the .NET tool can continue.", issues)
            };
        }

        progress?.Report(new MigrationProgress(30, "Checking required DBF tables..."));
        cancellationToken.ThrowIfCancellationRequested();

        var requiredTables = MigrationCatalog.GetRequiredTables(request.Options);
        var missingTables = requiredTables
            .Where(table => !File.Exists(Path.Combine(request.SourceDirectory, table)))
            .OrderBy(table => table, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingTables.Length > 0)
        {
            return new MigrationResult
            {
                IsSuccess = false,
                IsPreview = true,
                PlannedOutputs = plannedOutputs,
                Issues = missingTables,
                Summary = BuildIssueSummary(
                    "The selected exports rely on source tables that are missing from the KSV data directory.",
                    missingTables.Select(table => $"Missing table: {table}"))
            };
        }

        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var plannedOutputDirectory = Path.Combine(request.OutputDirectory, $"BottlePOS_CsvFiles_{timestamp}");
        var selectedExports = MigrationCatalog.GetSelectedExports(request.Options);
        var rowCache = new DbfRowCache(request.SourceDirectory, _dbfTableLoader);

        if (request.PreviewOnly)
        {
            progress?.Report(new MigrationProgress(100, "Preview ready."));

            return new MigrationResult
            {
                IsSuccess = true,
                IsPreview = true,
                PlannedOutputDirectory = plannedOutputDirectory,
                PlannedOutputs = plannedOutputs,
                Summary = BuildPreviewSummary(request, selectedExports, requiredTables, plannedOutputs, plannedOutputDirectory)
            };
        }

        progress?.Report(new MigrationProgress(45, "Creating output folder..."));
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(plannedOutputDirectory);

        var exportResults = new List<ExportExecution>();
        var zipFilePath = plannedOutputDirectory + ".zip";
        var temporaryZipFilePath = zipFilePath + ".tmp";

        try
        {
            if (request.Options.ExportDepartments)
            {
                progress?.Report(new MigrationProgress(52, "Generating departments..."));
                exportResults.Add(await ExportDepartmentsAsync(rowCache, plannedOutputDirectory, cancellationToken));
            }

            if (request.Options.ExportVendors)
            {
                progress?.Report(new MigrationProgress(58, "Generating vendors..."));
                exportResults.Add(await ExportVendorsAsync(rowCache, plannedOutputDirectory, cancellationToken));
            }

            if (request.Options.ExportCustomers)
            {
                progress?.Report(new MigrationProgress(64, "Generating customers..."));
                exportResults.Add(await ExportCustomersAsync(rowCache, plannedOutputDirectory, cancellationToken));
            }

            if (request.Options.ExportInventory)
            {
                progress?.Report(new MigrationProgress(78, "Generating inventory, inactive items, sale prices, and UPC link audit..."));
                exportResults.AddRange(await ExportInventoryBundleAsync(rowCache, plannedOutputDirectory, request.Options, cancellationToken));
            }

            if (request.Options.ExportGiftCards)
            {
                progress?.Report(new MigrationProgress(86, "Generating gift cards..."));
                exportResults.Add(await ExportGiftCardsAsync(rowCache, plannedOutputDirectory, cancellationToken));
            }

            progress?.Report(new MigrationProgress(94, "Creating ZIP archive..."));
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(temporaryZipFilePath))
            {
                File.Delete(temporaryZipFilePath);
            }

            ZipFile.CreateFromDirectory(plannedOutputDirectory, temporaryZipFilePath, CompressionLevel.Optimal, includeBaseDirectory: false);
            File.Move(temporaryZipFilePath, zipFilePath, overwrite: true);

            progress?.Report(new MigrationProgress(97, "Removing temporary output folder..."));
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new MigrationProgress(100, "Migration complete."));

            return new MigrationResult
            {
                IsSuccess = true,
                IsPreview = false,
                ZipFilePath = zipFilePath,
                PlannedOutputs = plannedOutputs,
                CreatedFiles = exportResults.Where(result => result.FileCreated).Select(result => result.FileName).ToArray(),
                Summary = BuildGenerationSummary(request, selectedExports, exportResults, plannedOutputDirectory, zipFilePath)
            };
        }
        finally
        {
            DeleteFileIfExists(temporaryZipFilePath);
            DeleteDirectoryIfExists(plannedOutputDirectory);
        }
    }

    private async Task<ExportExecution[]> ExportInventoryBundleAsync(
        DbfRowCache rowCache,
        string outputDirectory,
        ExportOptions options,
        CancellationToken cancellationToken)
    {
        var storeRows = rowCache.ReadRows("STR.DBF", "STORE");
        var store = storeRows.Count == 0 ? 0 : ToInt32(storeRows[0], "STORE");

        var cntRows = rowCache.ReadRows("CNT.DBF", "CODE", "DATA");
        var txcRows = rowCache.ReadRows("TXC.DBF", "CODE", "LEVEL", "RATE", "DESCRIPT");
        var vendorRows = rowCache.ReadRows("VND.DBF", "VCODE", "LASTNAME", "FIRSTNAME");
        var categoryRows = rowCache.ReadRows("CAT.DBF", "CAT", "NAME", "TAXLEVEL");
        var discountRows = rowCache.ReadRows("DSC.DBF", "DCODE", "LEVEL1DISC");
        var inventoryRows = rowCache.ReadRows("INV.DBF", "SKU", "NAME", "PACK", "TYPENAME", "SNAME", "DEPOS", "MEMO", "CAT", "FSFACTOR")
            .Select(ToInventorySourceRow)
            .ToList();
        var stockRows = rowCache.ReadRows("STK.DBF", "SKU", "STORE", "PVEND", "LVEND", "STAT", "FLOOR", "BACK", "ACOST", "LCOST", "MINCOST")
            .Select(ToStockSourceRow)
            .ToList();
        var priceRows = rowCache.ReadRows("PRC.DBF", "SKU", "STORE", "QTY", "PRICE", "LEVEL", "ONSALE", "SALE", "DCODE")
            .Select((row, index) => ToPriceSourceRow(row, index))
            .ToList();
        var upcRows = rowCache.ReadRows("UPC.DBF", "UPC", "SKU", "LAST", "LEVEL")
            .Select(ToUpcSourceRow)
            .ToList();

        var saleTaxCodeRow = cntRows.FirstOrDefault(row =>
            string.Equals(UpperTrim(GetString(row, "CODE")), "CUSTAX", StringComparison.OrdinalIgnoreCase));
        var saleTaxCode = UpperTrim(saleTaxCodeRow is null ? string.Empty : GetString(saleTaxCodeRow, "DATA"));

        var saleTaxRowsByLevel = string.IsNullOrWhiteSpace(saleTaxCode)
            ? new Dictionary<decimal, Dictionary<string, object?>>()
            : txcRows
                .Where(row => string.Equals(UpperTrim(GetString(row, "CODE")), saleTaxCode, StringComparison.OrdinalIgnoreCase))
                .GroupBy(row => ToDecimal(row, "LEVEL"))
                .ToDictionary(group => group.Key, group => group.First());

        var vendorNameByCode = vendorRows
            .Where(row => !string.IsNullOrWhiteSpace(GetString(row, "VCODE")))
            .GroupBy(row => UpperTrim(GetString(row, "VCODE")), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, BuildVendorDisplayName, StringComparer.OrdinalIgnoreCase);

        var categoryTaxLevelByCode = categoryRows
            .Where(row => !string.IsNullOrWhiteSpace(GetString(row, "CAT")))
            .GroupBy(row => UpperTrim(GetString(row, "CAT")), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => ToDecimal(group.First(), "TAXLEVEL"),
                StringComparer.OrdinalIgnoreCase);
        var nonDiscountableDiscountCodes = discountRows
            .Where(row => !string.IsNullOrWhiteSpace(GetString(row, "DCODE")))
            .Where(row => ToDecimal(row, "LEVEL1DISC") == 0m)
            .Select(row => UpperTrim(GetString(row, "DCODE")))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stockBySku = stockRows
            .Where(row => row.Store == store)
            .GroupBy(row => row.Sku)
            .ToDictionary(group => group.Key, BuildStockAggregate);

        var priceIndex = BuildPriceIndex(priceRows, store, options, nonDiscountableDiscountCodes);
        var upcBySku = BuildUpcBySku(upcRows);
        var upcLevelEntriesBySku = BuildUpcLevelEntriesBySku(upcRows);
        var vendorItemBySku = BuildVendorItemNumbersBySku(upcRows);

        var inventoryRowsForCsv = new List<IReadOnlyList<string>> { InventoryHeader };
        var inactiveRowsForCsv = new List<IReadOnlyList<string>> { InventoryHeader };
        var upcModifierLinkAuditRows = new List<UpcModifierLinkAuditRow>();

        foreach (var inventoryRow in inventoryRows.OrderBy(row => row.Sku))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sku = inventoryRow.Sku;
            stockBySku.TryGetValue(sku, out var stock);
            priceIndex.PricingBySku.TryGetValue(sku, out var pricingRows);
            upcBySku.TryGetValue(sku, out var productUpcs);
            upcLevelEntriesBySku.TryGetValue(sku, out var upcLevelEntries);
            vendorItemBySku.TryGetValue(sku, out var vendorItems);

            var pack = Math.Max(inventoryRow.Pack, 1m);
            var backStock = stock?.Back ?? 0m;
            var totalStock = backStock + (stock?.Floor ?? 0m);
            var averageCostPerUnit = CalculateUnitCost(stock?.Acost ?? 0m, pack);
            var lastCostPerUnit = CalculateUnitCost(stock?.Lcost ?? 0m, pack);

            var vendorCode = string.IsNullOrWhiteSpace(stock?.Lvend) ? stock?.Pvend ?? string.Empty : stock.Lvend;
            var vendorName = string.IsNullOrWhiteSpace(vendorCode) || !vendorNameByCode.TryGetValue(UpperTrim(vendorCode), out var foundVendorName)
                ? string.Empty
                : foundVendorName;

            var pricingSummary = BuildPricingSummary(pricingRows, averageCostPerUnit, lastCostPerUnit);
            var upcQuantityLinkSummary = BuildUpcQuantityLinkSummary(sku, pricingRows, upcLevelEntries);
            upcModifierLinkAuditRows.AddRange(upcQuantityLinkSummary.AuditRows);
            var isDiscountBlocked = priceIndex.NonDiscountableBySku.TryGetValue(sku, out var foundDiscountBlocked) &&
                foundDiscountBlocked;
            var unitSummary = BuildUnitSummary(inventoryRow.Sname, inventoryRow.Name);
            var inventoryQuantityDivisor = priceIndex.InventoryQuantityDivisorBySku.TryGetValue(sku, out var divisor)
                ? divisor
                : pricingSummary.DefaultQuantity;
            var unitsPerCaseValue = inventoryQuantityDivisor <= 0m
                ? pack
                : pack / inventoryQuantityDivisor;
            var effectivePackageQuantity = priceIndex.EffectivePackageQuantityBySku.TryGetValue(sku, out var quantity)
                ? quantity
                : pricingSummary.DefaultQuantity;
            var quantityValue = options.AddQuantityOneIfMissing
                ? totalStock
                : inventoryQuantityDivisor <= 0m
                    ? 0m
                    : Math.Truncate(backStock / inventoryQuantityDivisor);

            var depositValue = string.Empty;
            var depositCode = UpperTrim(inventoryRow.Depos);
            if (!string.IsNullOrWhiteSpace(depositCode))
            {
                depositValue = $"{FormatPackageQuantity(effectivePackageQuantity)}PK";
            }

            var hasCategoryTaxLevel = categoryTaxLevelByCode.TryGetValue(UpperTrim(inventoryRow.Cat), out var taxLevel);
            var isTaxable = hasCategoryTaxLevel && taxLevel > 0m;
            saleTaxRowsByLevel.TryGetValue(taxLevel, out var taxRow);
            var taxName = !isTaxable || taxRow is null
                ? string.Empty
                : CleanUpperText(
                    string.IsNullOrWhiteSpace(GetString(taxRow, "DESCRIPT"))
                        ? GetString(taxRow, "CODE")
                        : GetString(taxRow, "DESCRIPT"));

            var outputRow = new[]
            {
                "I",
                TrimToLength(RemoveCharacters(productUpcs is null ? string.Empty : string.Join(",", productUpcs), "\"-+@"), 250),
                TrimToLength(string.Join(",", upcQuantityLinkSummary.Links.Select(link => link.Upc)), 250),
                TrimToLength(string.Join(",", upcQuantityLinkSummary.Links.Select(link => FormatQuantity(link.Quantity))), 250),
                FormatSku(sku),
                TrimToLength(CleanUpperMultilineText(inventoryRow.Name), 100),
                TrimToLength(FormatMoney(averageCostPerUnit * pricingSummary.DefaultQuantity), 25),
                TrimToLength(FormatMoney(lastCostPerUnit * pricingSummary.DefaultQuantity), 50),
                TrimToLength(FormatMoney(pricingSummary.DefaultPrice), 25),
                TrimToLength(FormatPositiveOptionalMoney(stock?.MinCost), 25),
                isDiscountBlocked ? "TRUE" : string.Empty,
                FormatQuantity(quantityValue),
                TrimToLength(FormatInteger(unitsPerCaseValue), 25),
                FormatPointsMultiplier(inventoryRow.FsFactor),
                TrimToLength(taxName, 30),
                TrimToLength(isTaxable && taxRow is not null ? FormatRate(ToDecimal(taxRow, "RATE") * 100m) : string.Empty, 30),
                TrimToLength(string.IsNullOrWhiteSpace(inventoryRow.TypeName)
                    ? "MISC"
                    : CleanUpperMultilineText(inventoryRow.TypeName), 50),
                TrimToLength(string.IsNullOrWhiteSpace(vendorName) ? "UNKNOWN" : CleanUpperMultilineText(vendorName), 50),
                TrimToLength(vendorItems is null ? string.Empty : string.Join(",", vendorItems), 250),
                TrimToLength(unitSummary.Size, 10),
                TrimToLength(unitSummary.Type, 20),
                TrimToLength(pricingSummary.ModifierQuantities, 100),
                TrimToLength(pricingSummary.ModifierCosts, 100),
                TrimToLength(pricingSummary.ModifierLastCosts, 100),
                TrimToLength(pricingSummary.ModifierPrices, 100),
                CleanNotes(inventoryRow.Memo),
                TrimToLength(depositValue, 100)
            };

            var stockStatus = UpperTrim(stock?.Status ?? string.Empty);
            var isInactive = stockStatus is not "2" and not "8";

            if (options.IncludeInactiveProducts || !isInactive)
            {
                inventoryRowsForCsv.Add(outputRow);
            }

            if (isInactive)
            {
                inactiveRowsForCsv.Add(outputRow);
            }
        }

        var inventoryResult = await WriteExportAsync(
            outputDirectory,
            "Inventory",
            "4_inventory.csv",
            inventoryRowsForCsv,
            writeWhenHeaderOnly: false,
            cancellationToken);

        var inactiveResult = await WriteExportAsync(
            outputDirectory,
            "Inactive Items",
            "reference_InactiveItems.csv",
            inactiveRowsForCsv,
            writeWhenHeaderOnly: true,
            cancellationToken);

        var salePriceResult = await ExportSalePricesAsync(outputDirectory, store, priceRows, cancellationToken);
        var upcModifierLinkAuditResult = await ExportUpcModifierLinkAuditAsync(outputDirectory, upcModifierLinkAuditRows, cancellationToken);

        return new[] { inventoryResult, inactiveResult, salePriceResult, upcModifierLinkAuditResult };
    }

    private async Task<ExportExecution> ExportUpcModifierLinkAuditAsync(
        string outputDirectory,
        IReadOnlyList<UpcModifierLinkAuditRow> auditRows,
        CancellationToken cancellationToken)
    {
        var sortedAuditRows = SortUpcModifierLinkAuditRows(auditRows);
        var reportRows = sortedAuditRows
            .Select((row, index) => new UpcModifierLinkAuditReportRow(row, $"memo-{index + 1:00000}"))
            .ToList();

        var reportPath = Path.Combine(outputDirectory, UpcModifierLinkAuditFileName);
        await File.WriteAllTextAsync(
            reportPath,
            BuildUpcModifierLinkAuditHtml(reportRows),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken);

        return new ExportExecution(
            "UPC Modifier Link Audit",
            UpcModifierLinkAuditFileName,
            auditRows.Count,
            true);
    }

    private async Task<ExportExecution> ExportSalePricesAsync(
        string outputDirectory,
        int store,
        IReadOnlyList<PriceSourceRow> allPriceRows,
        CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "SKU", "SALE_PRICE", "REGULAR_PRICE" }
        };

        foreach (var priceRow in allPriceRows
                     .Where(row => row.Store == store && row.OnSale)
                     .OrderBy(row => row.Sku))
        {
            cancellationToken.ThrowIfCancellationRequested();

            rows.Add(new[]
            {
                FormatSku(priceRow.Sku),
                FormatPriceWithScale(priceRow.Sale, 4),
                FormatPriceWithScale(priceRow.Price, 4)
            });
        }

        return await WriteExportAsync(
            outputDirectory,
            "Sale Prices",
            "reference_SalePrices.csv",
            rows,
            writeWhenHeaderOnly: true,
            cancellationToken);
    }

    private async Task<ExportExecution> ExportDepartmentsAsync(
        DbfRowCache rowCache,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var categoryRows = rowCache.ReadRows("CAT.DBF", "CAT", "NAME", "TAXLEVEL");
        var typeRows = rowCache.ReadRows("TYP.DBF", "NAME", "SCAT1");

        var categoryGroupByCode = categoryRows
            .Where(row => !string.IsNullOrWhiteSpace(GetString(row, "CAT")))
            .GroupBy(row => UpperTrim(GetString(row, "CAT")), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => CleanUpperText(GetString(group.First(), "NAME")), StringComparer.OrdinalIgnoreCase);

        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "ImportType", "category_name", "category_group_name" }
        };

        foreach (var typeRow in typeRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var groupCode = UpperTrim(GetString(typeRow, "SCAT1"));
            if (string.IsNullOrWhiteSpace(groupCode) ||
                !categoryGroupByCode.TryGetValue(groupCode, out var categoryGroupName) ||
                string.IsNullOrWhiteSpace(categoryGroupName))
            {
                continue;
            }

            rows.Add(new[]
            {
                "D",
                CleanUpperText(GetString(typeRow, "NAME")),
                categoryGroupName
            });
        }

        return await WriteExportAsync(
            outputDirectory,
            "Department",
            "1_departments.csv",
            rows,
            writeWhenHeaderOnly: false,
            cancellationToken);
    }

    private async Task<ExportExecution> ExportVendorsAsync(
        DbfRowCache rowCache,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var vendorRows = rowCache.ReadRows(
            "VND.DBF",
            "VCODE",
            "LASTNAME",
            "FIRSTNAME",
            "CONTACT",
            "STREET1",
            "STREET2",
            "PHONE",
            "EMAIL");

        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "ImportType", "suppliername", "firstname", "address", "phone", "email" }
        };

        foreach (var vendorRow in vendorRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            rows.Add(new[]
            {
                "V",
                CleanUpperText($"{GetString(vendorRow, "LASTNAME")} {GetString(vendorRow, "FIRSTNAME")}"),
                CleanUpperText(GetString(vendorRow, "CONTACT")),
                CleanUpperText($"{GetString(vendorRow, "STREET1")} {GetString(vendorRow, "STREET2")}"),
                RemoveCharacters(GetString(vendorRow, "PHONE"), "\",+@#-()? "),
                CleanUpperText(GetString(vendorRow, "EMAIL"))
            });
        }

        return await WriteExportAsync(
            outputDirectory,
            "Vendor",
            "2_vendors.csv",
            rows,
            writeWhenHeaderOnly: false,
            cancellationToken);
    }

    private async Task<ExportExecution> ExportCustomersAsync(
        DbfRowCache rowCache,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var customerRows = rowCache.ReadRows(
            "CUS.DBF",
            "PHONE",
            "FIRSTNAME",
            "LASTNAME",
            "STREET1",
            "STREET2",
            "ZIP",
            "STATE",
            "FSCPTS",
            "CRDLIMIT",
            "BALANCE");

        var rows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "ImportType", "mobile", "firstname", "lastname", "address", "postcode", "state", "points", "houseacclimit", "housesaleborrow", "ishousepay"
            }
        };

        foreach (var customerRow in customerRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sanitizedPhone = RemoveCharacters(GetString(customerRow, "PHONE"), "\",+@#-()? ");
            var firstName = CleanUpperText(GetString(customerRow, "FIRSTNAME"));
            var lastName = CleanUpperText(GetString(customerRow, "LASTNAME"));

            if (string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName))
            {
                firstName = lastName;
                lastName = string.Empty;
            }

            rows.Add(new[]
            {
                "C",
                sanitizedPhone == "0" ? string.Empty : sanitizedPhone,
                firstName,
                lastName,
                CleanUpperText($"{GetString(customerRow, "STREET1")} {GetString(customerRow, "STREET2")}"),
                CleanUpperText(GetString(customerRow, "ZIP")),
                CleanUpperText(GetString(customerRow, "STATE")),
                FormatWholeNumber(ToDecimal(customerRow, "FSCPTS")),
                FormatWholeNumber(ToDecimal(customerRow, "CRDLIMIT")),
                FormatMoney(ToDecimal(customerRow, "BALANCE")),
                Math.Truncate(ToDecimal(customerRow, "CRDLIMIT")) > 0m ? "1" : "0"
            });
        }

        return await WriteExportAsync(
            outputDirectory,
            "Customer",
            "3_customers.csv",
            rows,
            writeWhenHeaderOnly: false,
            cancellationToken);
    }

    private async Task<ExportExecution> ExportGiftCardsAsync(
        DbfRowCache rowCache,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var giftCardRows = rowCache.ReadRows("GIFTCARD.DBF", "CRDNUM", "BALANCE");
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "ImportType", "Card_ID", "Balance" }
        };

        foreach (var giftCardRow in giftCardRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            rows.Add(new[]
            {
                "G",
                CleanUpperText(GetString(giftCardRow, "CRDNUM")),
                FormatMoney(ToDecimal(giftCardRow, "BALANCE"))
            });
        }

        return await WriteExportAsync(
            outputDirectory,
            "Giftcard",
            "5_gift_cards.csv",
            rows,
            writeWhenHeaderOnly: false,
            cancellationToken);
    }

    private async Task<ExportExecution> WriteExportAsync(
        string outputDirectory,
        string label,
        string fileName,
        IReadOnlyList<IReadOnlyList<string>> rows,
        bool writeWhenHeaderOnly,
        CancellationToken cancellationToken,
        int? recordCountOverride = null)
    {
        var filePath = Path.Combine(outputDirectory, fileName);
        var shouldWrite = writeWhenHeaderOnly || rows.Count > 1;

        if (shouldWrite)
        {
            await _csvFileWriter.WriteAsync(filePath, rows, cancellationToken);
        }

        return new ExportExecution(label, fileName, recordCountOverride ?? Math.Max(rows.Count - 1, 0), shouldWrite);
    }

    private static PriceIndex BuildPriceIndex(
        IReadOnlyList<PriceSourceRow> priceRows,
        int store,
        ExportOptions options,
        ISet<string> nonDiscountableDiscountCodes)
    {
        var pricingBySku = new Dictionary<int, List<PricingRow>>();
        var effectivePackageQuantityBySku = new Dictionary<int, decimal>();
        var inventoryQuantityDivisorBySku = new Dictionary<int, decimal>();
        var nonDiscountableBySku = new Dictionary<int, bool>();

        foreach (var group in BuildQualifiedPriceEntries(priceRows, options).GroupBy(entry => entry.Row.Sku))
        {
            var pricedEntries = GetPreferredStoreEntries(group, store, entry => entry.Row.Price > 0m);
            var discountEntries = GetPreferredStoreEntries(group, store, entry => !string.IsNullOrWhiteSpace(entry.Row.DiscountCode));
            var pricingRows = pricedEntries
                .Select(entry => new PricingRow(
                    group.Key,
                    entry.Row.Quantity,
                    entry.Row.Price,
                    entry.Row.Level,
                    entry.Sequence))
                .OrderBy(row => row.Quantity)
                .ThenBy(row => row.Sequence)
                .ToList();

            if (options.AddQuantityOneIfMissing && pricingRows.Count > 0 && pricingRows.All(row => row.Quantity != 1m))
            {
                var firstNonUnitRow = pricingRows[0];
                pricingRows.Add(new PricingRow(
                    group.Key,
                    1m,
                    VfpRound(firstNonUnitRow.Price / firstNonUnitRow.Quantity, 2),
                    options.DefaultPriceLevel,
                    -1));

                pricingRows = pricingRows
                    .OrderBy(row => row.Quantity)
                    .ThenBy(row => row.Sequence)
                    .ToList();
            }

            pricingBySku[group.Key] = pricingRows;

            var preferredQuantities = GetPreferredStoreEntries(group, store, static _ => true)
                .Select(entry => entry.Row.Quantity)
                .Where(quantity => quantity > 0m)
                .OrderBy(quantity => quantity)
                .ToList();

            effectivePackageQuantityBySku[group.Key] = preferredQuantities.Count == 0
                ? 1m
                : options.AddQuantityOneIfMissing && preferredQuantities.All(quantity => quantity != 1m)
                    ? 1m
                    : preferredQuantities[0];

            var pricedQuantities = pricedEntries
                .Select(entry => entry.Row.Quantity)
                .Where(quantity => quantity > 0m)
                .OrderBy(quantity => quantity)
                .ToList();

            inventoryQuantityDivisorBySku[group.Key] = pricedQuantities.Count > 0
                ? pricedQuantities[0]
                : preferredQuantities.Count == 0
                    ? 1m
                    : preferredQuantities[0];

            nonDiscountableBySku[group.Key] = discountEntries
                .Any(entry => nonDiscountableDiscountCodes.Contains(entry.Row.DiscountCode));
        }

        return new PriceIndex(pricingBySku, inventoryQuantityDivisorBySku, effectivePackageQuantityBySku, nonDiscountableBySku);
    }

    private static IEnumerable<PriceEntry> BuildQualifiedPriceEntries(
        IReadOnlyList<PriceSourceRow> priceRows,
        ExportOptions options)
    {
        return priceRows
            .Select(row => new PriceEntry(row, row.Sequence))
            .Where(entry => entry.Row.Quantity > 0m)
            .Where(entry =>
            {
                var level = entry.Row.Level;
                return options.UseDefaultPriceLevel
                    ? string.Equals(level, options.DefaultPriceLevel, StringComparison.OrdinalIgnoreCase)
                    : level is not "7" and not "8" and not "9";
            });
    }

    private static List<PriceEntry> GetPreferredStoreEntries(
        IEnumerable<PriceEntry> entries,
        int store,
        Func<PriceEntry, bool> predicate)
    {
        var matchingEntries = entries
            .Where(predicate)
            .ToList();

        if (matchingEntries.Count == 0)
        {
            return matchingEntries;
        }

        var exactStoreEntries = matchingEntries
            .Where(entry => entry.Row.Store == store)
            .ToList();

        if (exactStoreEntries.Count > 0)
        {
            return exactStoreEntries;
        }

        var primaryStoreEntries = matchingEntries
            .Where(entry => entry.Row.Store == 1)
            .ToList();

        return primaryStoreEntries.Count > 0
            ? primaryStoreEntries
            : matchingEntries;
    }

    private static IReadOnlyDictionary<int, List<string>> BuildUpcBySku(IReadOnlyList<UpcSourceRow> upcRows)
    {
        return upcRows
            .Select(row => new
            {
                row.Sku,
                Upc = UpperTrim(row.Upc)
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Upc) && entry.Upc.All(char.IsDigit))
            .GroupBy(entry => entry.Sku)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(entry => entry.Upc)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(upc => upc, StringComparer.OrdinalIgnoreCase)
                    .ToList());
    }

    private static IReadOnlyDictionary<int, List<string>> BuildVendorItemNumbersBySku(IReadOnlyList<UpcSourceRow> upcRows)
    {
        return upcRows
            .Select(row =>
            {
                var rawUpc = UpperTrim(row.Upc);
                return new
                {
                    row.Sku,
                    DigitsOnly = DigitsOnly(rawUpc),
                    row.LastSortValue,
                    ContainsNonDigits = !string.IsNullOrWhiteSpace(rawUpc) && rawUpc.Any(character => !char.IsDigit(character))
                };
            })
            .Where(entry => entry.ContainsNonDigits)
            .GroupBy(entry => entry.Sku)
            .ToDictionary(
                group => group.Key,
                group => group
                    .DistinctBy(entry => $"{entry.DigitsOnly}|{entry.LastSortValue}", StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(entry => entry.LastSortValue)
                    .ThenBy(entry => entry.DigitsOnly, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => entry.DigitsOnly)
                    .Where(upc => !string.IsNullOrWhiteSpace(upc) && upc.Length <= 10)
                    .ToList());
    }

    private static IReadOnlyDictionary<int, List<UpcLevelEntry>> BuildUpcLevelEntriesBySku(IReadOnlyList<UpcSourceRow> upcRows)
    {
        return upcRows
            .Select(row => new UpcLevelEntry(
                row.Sku,
                UpperTrim(row.Upc),
                row.Level))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Upc) && entry.Upc.All(char.IsDigit))
            .GroupBy(entry => entry.Sku)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(entry => $"{entry.Upc}|{entry.Level}", StringComparer.OrdinalIgnoreCase)
                    .Select(groupedEntry => groupedEntry.First())
                    .OrderBy(entry => entry.Level, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Upc, StringComparer.OrdinalIgnoreCase)
                    .ToList());
    }

    private static UpcQuantityLinkSummary BuildUpcQuantityLinkSummary(
        int sku,
        IReadOnlyList<PricingRow>? pricingRows,
        IReadOnlyList<UpcLevelEntry>? upcEntries)
    {
        var links = new List<UpcQuantityLink>();
        var auditRows = new List<UpcModifierLinkAuditRow>();

        if (upcEntries is null || upcEntries.Count == 0)
        {
            return new UpcQuantityLinkSummary(links, auditRows);
        }

        var distinctUpcEntries = upcEntries
            .GroupBy(entry => $"{entry.Upc}|{entry.Level}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var selectedQuantityRows = BuildSelectedQuantityRows(pricingRows);
        if (selectedQuantityRows.Count == 0)
        {
            foreach (var upcGroup in distinctUpcEntries.GroupBy(entry => entry.Upc, StringComparer.OrdinalIgnoreCase))
            {
                var upcLevels = JoinDistinctValues(upcGroup.Select(entry => entry.Level));
                auditRows.Add(new UpcModifierLinkAuditRow(
                    sku,
                    upcGroup.Key,
                    upcLevels,
                    string.Empty,
                    "No selected PRC quantity",
                    "No filtered PRC rows are available for this SKU, so no quantity can be linked."));
            }

            return new UpcQuantityLinkSummary(links, auditRows);
        }

        var multiLevelUpcs = distinctUpcEntries
            .GroupBy(entry => entry.Upc, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(entry => entry.Level).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .ToList();

        var multiLevelUpcCodes = multiLevelUpcs
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var upcGroup in multiLevelUpcs)
        {
            var upcLevels = JoinDistinctValues(upcGroup.Select(entry => entry.Level));
            auditRows.Add(new UpcModifierLinkAuditRow(
                sku,
                upcGroup.Key,
                upcLevels,
                string.Empty,
                "UPC assigned to multiple levels",
                $"This UPC appears under UPC.LEVEL values {upcLevels}; one code cannot be tied to a single quantity."));
        }

        var selectedRowsByLevel = selectedQuantityRows
            .Where(row => !string.IsNullOrWhiteSpace(row.Level))
            .GroupBy(row => row.Level, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ambiguousPrcLevels = selectedRowsByLevel
            .Where(group => group.Select(row => row.Quantity).Distinct().Count() > 1)
            .ToDictionary(
                group => group.Key,
                group => JoinDistinctValues(group.Select(row => FormatQuantity(row.Quantity))),
                StringComparer.OrdinalIgnoreCase);

        var prcQuantityByLevel = selectedRowsByLevel
            .Where(group => !ambiguousPrcLevels.ContainsKey(group.Key))
            .ToDictionary(
                group => group.Key,
                group => group.First().Quantity,
                StringComparer.OrdinalIgnoreCase);

        var selectedPrcLevels = JoinDistinctValues(selectedRowsByLevel.Select(group => group.Key));

        foreach (var upcEntry in distinctUpcEntries
                     .Where(entry => !multiLevelUpcCodes.Contains(entry.Upc))
                     .OrderBy(entry => entry.Level, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(entry => entry.Upc, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(upcEntry.Level))
            {
                auditRows.Add(new UpcModifierLinkAuditRow(
                    sku,
                    upcEntry.Upc,
                    string.Empty,
                    string.Empty,
                    "Blank UPC level",
                    "UPC.LEVEL is blank, so the utility has no level to compare with PRC.LEVEL."));
                continue;
            }

            if (ambiguousPrcLevels.TryGetValue(upcEntry.Level, out var ambiguousQuantities))
            {
                auditRows.Add(new UpcModifierLinkAuditRow(
                    sku,
                    upcEntry.Upc,
                    upcEntry.Level,
                    upcEntry.Level,
                    "PRC level has multiple quantities",
                    $"PRC.LEVEL {upcEntry.Level} maps to quantities {ambiguousQuantities}; the level does not identify one clear quantity."));
                continue;
            }

            if (!prcQuantityByLevel.TryGetValue(upcEntry.Level, out var linkedQuantity))
            {
                auditRows.Add(new UpcModifierLinkAuditRow(
                    sku,
                    upcEntry.Upc,
                    upcEntry.Level,
                    string.Empty,
                    "No matching PRC level",
                    $"UPC.LEVEL {upcEntry.Level} did not match any selected PRC.LEVEL for this SKU. Selected PRC levels: {(string.IsNullOrWhiteSpace(selectedPrcLevels) ? "none" : selectedPrcLevels)}."));
                continue;
            }

            links.Add(new UpcQuantityLink(upcEntry.Upc, upcEntry.Level, linkedQuantity));
        }

        var duplicateQuantityLinks = links
            .GroupBy(link => link.Quantity)
            .Where(group => group.Select(link => link.Upc).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .ToList();

        var duplicateQuantityLinkKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var linkGroup in duplicateQuantityLinks)
        {
            var upcsForQuantity = JoinDistinctValues(linkGroup.Select(link => link.Upc));
            var quantityText = FormatQuantity(linkGroup.Key);

            foreach (var link in linkGroup)
            {
                duplicateQuantityLinkKeys.Add($"{link.Upc}|{link.Quantity}");
                auditRows.Add(new UpcModifierLinkAuditRow(
                    sku,
                    link.Upc,
                    link.Level,
                    link.Level,
                    "Multiple UPCs for same quantity",
                    $"Quantity {quantityText} matched multiple UPC codes ({upcsForQuantity}); CodeToQTY requires one clear UPC per quantity."));
            }
        }

        var orderedLinks = links
            .Where(link => !duplicateQuantityLinkKeys.Contains($"{link.Upc}|{link.Quantity}"))
            .OrderBy(link => link.Quantity)
            .ThenBy(link => link.Upc, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var orderedAuditRows = auditRows
            .OrderBy(row => row.Sku)
            .ThenBy(row => row.UpcCodes, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.UpcLevel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new UpcQuantityLinkSummary(orderedLinks, orderedAuditRows);
    }

    private static List<SelectedQuantityRow> BuildSelectedQuantityRows(IReadOnlyList<PricingRow>? pricingRows)
    {
        var selectedRows = new List<SelectedQuantityRow>();

        if (pricingRows is null || pricingRows.Count == 0)
        {
            return selectedRows;
        }

        var seenQuantities = new HashSet<decimal>();
        foreach (var pricingRow in pricingRows)
        {
            if (!seenQuantities.Add(pricingRow.Quantity))
            {
                continue;
            }

            selectedRows.Add(new SelectedQuantityRow(pricingRow.Level, pricingRow.Quantity));
        }

        return selectedRows;
    }

    private static List<UpcModifierLinkAuditRow> SortUpcModifierLinkAuditRows(IReadOnlyList<UpcModifierLinkAuditRow> auditRows)
    {
        return auditRows
            .OrderBy(row => row.Sku)
            .ThenBy(row => row.UpcCodes, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.UpcLevel, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildUpcModifierLinkAuditHtml(IReadOnlyList<UpcModifierLinkAuditReportRow> reportRows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<title>UPC Modifier Link Audit</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1f2933;background:#fff}");
        builder.AppendLine("h1{font-size:22px;margin:0 0 8px}");
        builder.AppendLine("p{margin:0 0 14px;color:#52606d}");
        builder.AppendLine("table{border-collapse:collapse;width:100%;font-size:13px}");
        builder.AppendLine("th,td{border:1px solid #d9e2ec;padding:7px 8px;text-align:left;vertical-align:top}");
        builder.AppendLine("th{background:#f0f4f8;color:#243b53;position:sticky;top:0}");
        builder.AppendLine("tr:nth-child(even){background:#f9fbfd}");
        builder.AppendLine("a{color:#0b5cad;text-decoration:none;font-weight:600}");
        builder.AppendLine("a:hover{text-decoration:underline}");
        builder.AppendLine(".summary{margin:18px 0 22px;max-width:640px}");
        builder.AppendLine(".summary td:first-child{width:90px;text-align:right;font-weight:600}");
        builder.AppendLine(".memo-list{margin-top:28px}");
        builder.AppendLine(".memo{border:1px solid #d9e2ec;border-radius:6px;margin:0 0 16px;padding:14px;background:#f9fbfd}");
        builder.AppendLine(".memo h2{font-size:15px;margin:0 0 10px;color:#243b53}");
        builder.AppendLine(".memo pre{margin:0;white-space:pre-wrap;font-family:Consolas,monospace;font-size:13px;line-height:1.45;color:#1f2933}");
        builder.AppendLine(".back-link{display:inline-block;margin-top:10px;font-size:12px}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<h1>UPC Modifier Link Audit</h1>");
        builder.AppendLine("<p>This report lists only UPC codes that could not be safely linked to one modifier quantity. Open the ISSUE link for the row memo.</p>");
        builder.AppendLine("<p>The memo text is embedded in this report, so no separate memo files are required.</p>");

        if (reportRows.Count == 0)
        {
            builder.AppendLine("<p>No unlinkable UPC codes were found for this run.</p>");
        }
        else
        {
            builder.AppendLine("<table class=\"summary\">");
            builder.AppendLine("<thead><tr><th>Count</th><th>Issue</th></tr></thead>");
            builder.AppendLine("<tbody>");
            foreach (var issueGroup in reportRows
                         .GroupBy(row => row.AuditRow.Issue)
                         .OrderByDescending(group => group.Count())
                         .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine("<tr>");
                builder.Append("<td>").Append(issueGroup.Count().ToString(CultureInfo.InvariantCulture)).AppendLine("</td>");
                builder.Append("<td>").Append(HtmlEncode(issueGroup.Key)).AppendLine("</td>");
                builder.AppendLine("</tr>");
            }

            builder.AppendLine("</tbody>");
            builder.AppendLine("</table>");
        }

        builder.AppendLine("<table id=\"audit-table\">");
        builder.AppendLine("<thead><tr>");
        foreach (var column in UpcModifierLinkAuditHeader)
        {
            builder.Append("<th>").Append(HtmlEncode(column)).AppendLine("</th>");
        }

        builder.AppendLine("</tr></thead>");
        builder.AppendLine("<tbody>");

        foreach (var reportRow in reportRows)
        {
            var auditRow = reportRow.AuditRow;
            builder.AppendLine("<tr>");
            AppendHtmlCell(builder, FormatSku(auditRow.Sku));
            AppendHtmlCell(builder, auditRow.UpcCodes);
            AppendHtmlCell(builder, auditRow.UpcLevel);
            AppendHtmlCell(builder, auditRow.PrcLevel);
            builder.Append("<td><a href=\"")
                .Append("#")
                .Append(HtmlEncode(reportRow.MemoId))
                .Append("\">MEMO - ")
                .Append(HtmlEncode(auditRow.Issue))
                .AppendLine("</a></td>");
            builder.AppendLine("</tr>");
        }

        builder.AppendLine("</tbody>");
        builder.AppendLine("</table>");

        if (reportRows.Count > 0)
        {
            builder.AppendLine("<div class=\"memo-list\">");
            builder.AppendLine("<h1>Row Memos</h1>");
            foreach (var reportRow in reportRows)
            {
                var auditRow = reportRow.AuditRow;
                builder.Append("<section class=\"memo\" id=\"")
                    .Append(HtmlEncode(reportRow.MemoId))
                    .AppendLine("\">");
                builder.Append("<h2>")
                    .Append(HtmlEncode($"{FormatSku(auditRow.Sku)} - {auditRow.UpcCodes} - {auditRow.Issue}"))
                    .AppendLine("</h2>");
                builder.Append("<pre>")
                    .Append(HtmlEncode(BuildAuditMemoText(auditRow)))
                    .AppendLine("</pre>");
                builder.AppendLine("<a class=\"back-link\" href=\"#audit-table\">Back to audit table</a>");
                builder.AppendLine("</section>");
            }

            builder.AppendLine("</div>");
        }

        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static void AppendHtmlCell(StringBuilder builder, string value)
    {
        builder.Append("<td>").Append(HtmlEncode(value)).AppendLine("</td>");
    }

    private static string BuildAuditMemoText(UpcModifierLinkAuditRow auditRow)
    {
        var builder = new StringBuilder();
        builder.AppendLine("UPC MODIFIER LINK AUDIT MEMO");
        builder.AppendLine();
        builder.AppendLine($"Issue: {auditRow.Issue}");
        builder.AppendLine($"SKU: {FormatSku(auditRow.Sku)}");
        builder.AppendLine($"UPC: {auditRow.UpcCodes}");
        builder.AppendLine($"UPC_LEVEL: {BlankForMemo(auditRow.UpcLevel)}");
        builder.AppendLine($"PRC_LEVEL: {BlankForMemo(auditRow.PrcLevel)}");
        builder.AppendLine();
        builder.AppendLine("Explanation:");
        builder.AppendLine(auditRow.Memo);
        builder.AppendLine();
        builder.AppendLine("Output behavior:");
        builder.AppendLine("This UPC remains in the existing CODE column. It was not added to CodeToQTY or LinkedQTY because the utility could not prove one clear UPC-to-quantity link.");
        return builder.ToString();
    }

    private static string BlankForMemo(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(blank)" : value;
    }

    private static string HtmlEncode(string value)
    {
        return System.Net.WebUtility.HtmlEncode(value);
    }

    private static string JoinDistinctValues(IEnumerable<string> values)
    {
        return string.Join(
            ",",
            values
                .Select(UpperTrim)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }

    private static PricingSummary BuildPricingSummary(IReadOnlyList<PricingRow>? pricingRows, decimal averageCostPerUnit, decimal lastCostPerUnit)
    {
        if (pricingRows is null || pricingRows.Count == 0)
        {
            return new PricingSummary(1, 0m, string.Empty, string.Empty, string.Empty, string.Empty);
        }

        var seenQuantities = new HashSet<decimal>();
        var defaultQuantity = 1;
        var defaultPrice = 0m;
        var baseQuantity = 1m;
        var modifierQuantities = new List<string>();
        var modifierPrices = new List<string>();
        var modifierCosts = new List<string>();
        var modifierLastCosts = new List<string>();
        var isFirstUniqueQuantity = true;

        foreach (var pricingRow in pricingRows)
        {
            if (!seenQuantities.Add(pricingRow.Quantity))
            {
                continue;
            }

            if (isFirstUniqueQuantity)
            {
                defaultQuantity = pricingRow.Quantity == 0m ? 1 : (int)Math.Round(pricingRow.Quantity, 0, MidpointRounding.AwayFromZero);
                baseQuantity = pricingRow.Quantity == 0m ? 1m : pricingRow.Quantity;
                defaultPrice = pricingRow.Price;
                isFirstUniqueQuantity = false;
                continue;
            }

            var modifierQuantity = baseQuantity == 1m
                ? pricingRow.Quantity
                : Math.Round(pricingRow.Quantity / baseQuantity, 0, MidpointRounding.AwayFromZero);

            modifierQuantities.Add(FormatWholeNumber(modifierQuantity));
            modifierPrices.Add(FormatMoney(pricingRow.Price));
            modifierCosts.Add(FormatMoney(averageCostPerUnit * pricingRow.Quantity));
            modifierLastCosts.Add(FormatMoney(lastCostPerUnit * pricingRow.Quantity));
        }

        if (defaultQuantity == 0)
        {
            defaultQuantity = 1;
        }

        return new PricingSummary(
            defaultQuantity,
            defaultPrice,
            string.Join(",", modifierQuantities),
            string.Join(",", modifierCosts),
            string.Join(",", modifierLastCosts),
            string.Join(",", modifierPrices));
    }

    private static StockAggregate BuildStockAggregate(IGrouping<int, StockSourceRow> group)
    {
        return new StockAggregate(
            MaxString(group.Select(row => row.Pvend)),
            MaxString(group.Select(row => row.Lvend)),
            MaxString(group.Select(row => row.Status)),
            group.Max(row => row.Floor),
            group.Max(row => row.Back),
            group.Max(row => row.Acost),
            group.Max(row => row.Lcost),
            MaxNullableDecimal(group.Select(row => row.MinCost)));
    }

    private static UnitSummary BuildUnitSummary(string sname, string name)
    {
        var normalizedSname = UpperTrim(sname);
        if (string.IsNullOrWhiteSpace(normalizedSname) || normalizedSname == "N/A")
        {
            var packDescription = UpperTrim(name)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace("PACK", "PK", StringComparison.Ordinal)
                .Replace(" PK", "PK", StringComparison.Ordinal);

            var packSize = 0;
            if (packDescription.Contains("PK", StringComparison.Ordinal))
            {
                for (var index = 1; index <= 80; index++)
                {
                    if (packDescription.Contains($"{index}PK", StringComparison.Ordinal))
                    {
                        packSize = index;
                    }
                }
            }

            return packSize > 0
                ? new UnitSummary(packSize.ToString(CultureInfo.InvariantCulture), "PK")
                : new UnitSummary("1", "N/A");
        }

        var unitType = new string(normalizedSname
            .Where(character => !char.IsDigit(character) && !" /@#$%^&*+=-.\\?,".Contains(character))
            .ToArray());

        var unitSize = new string(normalizedSname
            .Where(character => !char.IsLetter(character) && !" @#\\/?, ".Contains(character))
            .ToArray());

        if (unitType is "O" or "Z")
        {
            unitType = "OZ";
        }

        if (!string.IsNullOrWhiteSpace(unitSize) && string.IsNullOrWhiteSpace(unitType))
        {
            if (unitSize is "750" or "500")
            {
                unitType = "ML";
            }

            if (unitSize is "1.5" or "1.75")
            {
                unitType = "L";
            }
        }

        if (string.IsNullOrWhiteSpace(unitSize))
        {
            unitSize = "1";
        }

        if (string.IsNullOrWhiteSpace(unitType))
        {
            unitType = "N/A";
        }

        return new UnitSummary(unitSize, unitType);
    }

    private static IEnumerable<string> ValidateRequest(MigrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceDirectory))
        {
            yield return "Choose a Spirits/KSV data directory.";
        }
        else if (!Directory.Exists(request.SourceDirectory))
        {
            yield return "The selected Spirits/KSV data directory does not exist.";
        }

        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            yield return "Choose an output directory.";
        }
        else if (!Directory.Exists(request.OutputDirectory))
        {
            yield return "The selected output directory does not exist.";
        }

        if (!request.Options.HasSelections)
        {
            yield return "Select at least one export before starting the migration.";
        }

        if (request.Options.UseDefaultPriceLevel &&
            !new[] { "1", "2", "3" }.Contains(request.Options.DefaultPriceLevel, StringComparer.Ordinal))
        {
            yield return "Default price level must be 1, 2, or 3.";
        }
    }

    private static string BuildIssueSummary(string intro, IEnumerable<string> issues)
    {
        var builder = new StringBuilder();
        builder.AppendLine(intro);
        builder.AppendLine();

        foreach (var issue in issues)
        {
            builder.AppendLine($"- {issue}");
        }

        builder.AppendLine();
        builder.AppendLine("No files were written.");
        return builder.ToString().TrimEnd();
    }

    private static string BuildPreviewSummary(
        MigrationRequest request,
        IReadOnlyList<string> selectedExports,
        IReadOnlyList<string> requiredTables,
        IReadOnlyList<string> plannedOutputs,
        string plannedOutputDirectory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("The .NET migration utility validated your inputs successfully.");
        builder.AppendLine("This preview confirms the source tables and planned output files before a generation run.");
        builder.AppendLine();
        builder.AppendLine($"Source directory: {request.SourceDirectory}");
        builder.AppendLine($"Output directory: {request.OutputDirectory}");
        builder.AppendLine($"Planned output folder: {plannedOutputDirectory}");
        builder.AppendLine();
        builder.AppendLine("Selected exports:");

        foreach (var export in selectedExports)
        {
            builder.AppendLine($"- {export}");
        }

        if (request.Options.ExportInventory)
        {
            builder.AppendLine();
            builder.AppendLine("Inventory behavior:");
            builder.AppendLine($"- Main inventory will {(request.Options.IncludeInactiveProducts ? "include" : "exclude")} inactive items.");
            builder.AppendLine($"- Missing QTY=1 rows will {(request.Options.AddQuantityOneIfMissing ? "be added" : "not be added")}.");
            builder.AppendLine("- bottledeposit will use the smallest effective quantity followed by PK when an item has a deposit code.");
            builder.AppendLine("- CodeToQTY and LinkedQTY will show UPCs that can be linked one-to-one with selected PRC quantities.");
            builder.AppendLine("- reference_InactiveItems.csv is always generated when inventory is selected.");
            builder.AppendLine("- reference_SalePrices.csv contains SKU, SALE_PRICE, and REGULAR_PRICE.");
            builder.AppendLine("- reference_UPCModifierLinkAudit.html contains only UPC codes that could not be linked, with memo links in the ISSUE column.");
        }

        builder.AppendLine();
        builder.AppendLine("Planned output files:");

        foreach (var output in plannedOutputs)
        {
            builder.AppendLine($"- {output}");
        }

        builder.AppendLine();
        builder.AppendLine("Required source tables found:");

        foreach (var table in requiredTables)
        {
            builder.AppendLine($"- {table}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildGenerationSummary(
        MigrationRequest request,
        IReadOnlyList<string> selectedExports,
        IReadOnlyList<ExportExecution> exportResults,
        string outputDirectory,
        string zipFilePath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("The .NET migration run completed successfully.");
        builder.AppendLine();
        builder.AppendLine($"Source directory: {request.SourceDirectory}");
        builder.AppendLine($"ZIP archive: {zipFilePath}");
        builder.AppendLine();
        builder.AppendLine("Selected exports:");

        foreach (var export in selectedExports)
        {
            builder.AppendLine($"- {export}");
        }

        builder.AppendLine();
        builder.AppendLine("Export results:");

        foreach (var exportResult in exportResults)
        {
            builder.AppendLine(
                $"- {exportResult.Label}: {exportResult.RecordCount} record(s){(exportResult.FileCreated ? $" -> {exportResult.FileName}" : " -> no file written")}");
        }

        if (request.Options.ExportInventory)
        {
            builder.AppendLine();
            builder.AppendLine("Inventory behavior:");
            builder.AppendLine($"- Main inventory {(request.Options.IncludeInactiveProducts ? "included" : "excluded")} inactive items.");
            builder.AppendLine($"- Missing QTY=1 rows were {(request.Options.AddQuantityOneIfMissing ? "added when needed" : "left unchanged")}.");
            builder.AppendLine("- bottledeposit used the smallest effective quantity followed by PK when an item had a deposit code.");
            builder.AppendLine("- CodeToQTY and LinkedQTY were populated for UPCs that matched selected PRC quantities one-to-one.");
            builder.AppendLine("- Inactive items were also written to reference_InactiveItems.csv.");
            builder.AppendLine("- Sale prices were written without the ON_SALE column.");
            builder.AppendLine("- Unlinkable UPC codes were written to reference_UPCModifierLinkAudit.html with row memo links.");
        }

        builder.AppendLine();
        builder.AppendLine($"Temporary output folder: {outputDirectory}");
        builder.AppendLine("The output files were packaged into the ZIP archive and the temporary folder was removed.");

        return builder.ToString().TrimEnd();
    }

    private static void DeleteFileIfExists(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch (IOException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not delete temporary file: {filePath}");
        }
        catch (UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"No permission to delete temporary file: {filePath}");
        }
    }

    private static void DeleteDirectoryIfExists(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch (IOException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not delete temporary folder: {directoryPath}");
        }
        catch (UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"No permission to delete temporary folder: {directoryPath}");
        }
    }

    private static InventorySourceRow ToInventorySourceRow(IReadOnlyDictionary<string, object?> row)
    {
        return new InventorySourceRow(
            ToInt32(row, "SKU"),
            GetString(row, "NAME"),
            ToDecimal(row, "PACK"),
            GetString(row, "TYPENAME"),
            GetString(row, "SNAME"),
            GetString(row, "DEPOS"),
            GetString(row, "MEMO"),
            GetString(row, "CAT"),
            ToNullableDecimal(row, "FSFACTOR"));
    }

    private static StockSourceRow ToStockSourceRow(IReadOnlyDictionary<string, object?> row)
    {
        return new StockSourceRow(
            ToInt32(row, "SKU"),
            ToInt32(row, "STORE"),
            GetString(row, "PVEND"),
            GetString(row, "LVEND"),
            GetString(row, "STAT"),
            ToDecimal(row, "FLOOR"),
            ToDecimal(row, "BACK"),
            ToDecimal(row, "ACOST"),
            ToDecimal(row, "LCOST"),
            ToNullableDecimal(row, "MINCOST"));
    }

    private static PriceSourceRow ToPriceSourceRow(IReadOnlyDictionary<string, object?> row, int sequence)
    {
        return new PriceSourceRow(
            ToInt32(row, "SKU"),
            ToInt32(row, "STORE"),
            ToDecimal(row, "QTY"),
            ToDecimal(row, "PRICE"),
            UpperTrim(GetString(row, "LEVEL")),
            ToBoolean(row, "ONSALE"),
            ToDecimal(row, "SALE"),
            UpperTrim(GetString(row, "DCODE")),
            sequence);
    }

    private static UpcSourceRow ToUpcSourceRow(IReadOnlyDictionary<string, object?> row)
    {
        return new UpcSourceRow(
            ToInt32(row, "SKU"),
            GetString(row, "UPC"),
            UpperTrim(GetString(row, "LEVEL")),
            ToSortValue(row, "LAST"));
    }

    private static string BuildVendorDisplayName(IEnumerable<Dictionary<string, object?>> rows)
    {
        var row = rows.First();
        var lastName = UpperTrim(GetString(row, "LASTNAME"));
        var firstName = UpperTrim(GetString(row, "FIRSTNAME"));
        return string.IsNullOrWhiteSpace(lastName)
            ? firstName
            : $"{lastName}{(string.IsNullOrWhiteSpace(firstName) ? string.Empty : $" {firstName}")}";
    }

    private static string MaxString(IEnumerable<string> values)
    {
        return values
            .Select(UpperTrim)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .LastOrDefault() ?? string.Empty;
    }

    private static decimal? MaxNullableDecimal(IEnumerable<decimal?> values)
    {
        var numericValues = values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        return numericValues.Count == 0
            ? null
            : numericValues.Max();
    }

    private static string GetString(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        if (!row.TryGetValue(columnName, out var value) || value is null || value is DBNull)
        {
            return string.Empty;
        }

        return value switch
        {
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
            double doubleValue => doubleValue.ToString(CultureInfo.InvariantCulture),
            float floatValue => floatValue.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static decimal ToDecimal(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        if (!row.TryGetValue(columnName, out var value) || value is null || value is DBNull)
        {
            return 0m;
        }

        return value switch
        {
            decimal decimalValue => decimalValue,
            double doubleValue => Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture),
            float floatValue => Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture),
            byte byteValue => byteValue,
            short shortValue => shortValue,
            int intValue => intValue,
            long longValue => longValue,
            string stringValue when decimal.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue) => parsedValue,
            string stringValue when decimal.TryParse(stringValue, NumberStyles.Any, CultureInfo.CurrentCulture, out var currentCultureValue) => currentCultureValue,
            _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
        };
    }

    private static decimal? ToNullableDecimal(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        if (!row.TryGetValue(columnName, out var value) || value is null || value is DBNull)
        {
            return null;
        }

        if (value is string rawStringValue && string.IsNullOrWhiteSpace(rawStringValue))
        {
            return null;
        }

        return value switch
        {
            decimal decimalValue => decimalValue,
            double doubleValue => Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture),
            float floatValue => Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture),
            byte byteValue => byteValue,
            short shortValue => shortValue,
            int intValue => intValue,
            long longValue => longValue,
            string stringValue when decimal.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue) => parsedValue,
            string stringValue when decimal.TryParse(stringValue, NumberStyles.Any, CultureInfo.CurrentCulture, out var currentCultureValue) => currentCultureValue,
            _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
        };
    }

    private static int ToInt32(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        return Convert.ToInt32(decimal.Truncate(ToDecimal(row, columnName)), CultureInfo.InvariantCulture);
    }

    private static long ToSortValue(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        if (!row.TryGetValue(columnName, out var value) || value is null || value is DBNull)
        {
            return 0;
        }

        return value switch
        {
            DateTime dateTime => dateTime.Ticks,
            decimal decimalValue => Convert.ToInt64(decimal.Truncate(decimalValue), CultureInfo.InvariantCulture),
            double doubleValue => Convert.ToInt64(Math.Truncate(doubleValue), CultureInfo.InvariantCulture),
            float floatValue => Convert.ToInt64(Math.Truncate(floatValue), CultureInfo.InvariantCulture),
            byte byteValue => byteValue,
            short shortValue => shortValue,
            int intValue => intValue,
            long longValue => longValue,
            string stringValue when long.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedValue) => parsedValue,
            _ => 0
        };
    }

    private static bool ToBoolean(IReadOnlyDictionary<string, object?> row, string columnName)
    {
        if (!row.TryGetValue(columnName, out var value) || value is null || value is DBNull)
        {
            return false;
        }

        return value switch
        {
            bool booleanValue => booleanValue,
            string stringValue => stringValue.Equals("T", StringComparison.OrdinalIgnoreCase) ||
                                  stringValue.Equals(".T.", StringComparison.OrdinalIgnoreCase) ||
                                  stringValue.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
                                  stringValue.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
                                  stringValue.Equals("1", StringComparison.OrdinalIgnoreCase),
            _ => Convert.ToBoolean(value, CultureInfo.InvariantCulture)
        };
    }

    private static string FormatSku(int sku)
    {
        var value = sku.ToString(CultureInfo.InvariantCulture).Trim();
        return value.Length < 6
            ? value.PadLeft(5, '0')
            : value;
    }

    private static string FormatMoney(decimal value)
    {
        return VfpRound(value, 2).ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatPositiveOptionalMoney(decimal? value)
    {
        return value.HasValue && value.Value > 0m
            ? FormatMoney(value.Value)
            : string.Empty;
    }

    private static string FormatRate(decimal value)
    {
        return VfpRound(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatQuantity(decimal value)
    {
        return VfpRound(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatPositiveOptionalDecimal(decimal? value)
    {
        return value.HasValue && value.Value > 0m
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string FormatPointsMultiplier(decimal? value)
    {
        if (!value.HasValue)
        {
            return "1";
        }

        var multiplier = value.Value;
        return multiplier >= 1m && multiplier <= 5m && decimal.Truncate(multiplier) == multiplier
            ? multiplier.ToString("0", CultureInfo.InvariantCulture)
            : "1";
    }

    private static string FormatInteger(decimal value)
    {
        return Math.Truncate(value).ToString("0", CultureInfo.InvariantCulture);
    }

    private static string FormatWholeNumber(decimal value)
    {
        return Math.Truncate(value).ToString("0", CultureInfo.InvariantCulture);
    }

    private static string FormatPackageQuantity(decimal value)
    {
        var roundedValue = VfpRound(value, 3);
        var format = roundedValue == Math.Truncate(roundedValue) ? "0" : "0.###";
        return roundedValue.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string CleanUpperText(string? value)
    {
        return RemoveCharacters(UpperTrim(value), "\",");
    }

    private static string CleanUpperMultilineText(string? value)
    {
        return ReplaceLineBreaks(RemoveCharacters(UpperTrim(value), "\","), " ");
    }

    private static string CleanNotes(string? value)
    {
        var notes = ReplaceLineBreaks(value ?? string.Empty, " ");
        notes = notes.Length > 250 ? notes[..250] : notes;

        while (notes.Contains("  ", StringComparison.Ordinal))
        {
            notes = notes.Replace("  ", " ", StringComparison.Ordinal);
        }

        return TrimToLength(CleanUpperText(notes), 100);
    }

    private static decimal VfpRound(decimal value, int digits)
    {
        return Math.Round(value, digits, MidpointRounding.AwayFromZero);
    }

    private static decimal CalculateUnitCost(decimal totalCost, decimal pack)
    {
        if (pack == 0m)
        {
            return 0m;
        }

        return Math.Round(totalCost / pack, 4, MidpointRounding.ToEven);
    }

    private static string FormatPriceWithScale(decimal value, int scale)
    {
        var format = "0." + new string('0', scale);
        return VfpRound(value, scale).ToString(format, CultureInfo.InvariantCulture);
    }

    private static string TrimToLength(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }

    private static string UpperTrim(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string ReplaceLineBreaks(string value, string replacement)
    {
        return value
            .Replace("\r\n", replacement, StringComparison.Ordinal)
            .Replace("\r", replacement, StringComparison.Ordinal)
            .Replace("\n", replacement, StringComparison.Ordinal);
    }

    private static string RemoveCharacters(string value, string charactersToRemove)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var filtered = value.Where(character => !charactersToRemove.Contains(character)).ToArray();
        return new string(filtered).Trim();
    }

    private static string DigitsOnly(string value)
    {
        return new string(value.Where(char.IsDigit).ToArray());
    }

    private sealed class DbfRowCache
    {
        private readonly string _sourceDirectory;
        private readonly DbfTableLoader _loader;
        private readonly Dictionary<string, CachedTable> _tables = new(StringComparer.OrdinalIgnoreCase);

        public DbfRowCache(string sourceDirectory, DbfTableLoader loader)
        {
            _sourceDirectory = sourceDirectory;
            _loader = loader;
        }

        public IReadOnlyList<Dictionary<string, object?>> ReadRows(string tableName, params string[] columns)
        {
            if (_tables.TryGetValue(tableName, out var cachedTable) &&
                columns.All(column => cachedTable.Columns.Contains(column)))
            {
                return cachedTable.Rows;
            }

            var requestedColumns = cachedTable is null
                ? columns
                : cachedTable.Columns.Concat(columns).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            var rows = _loader.ReadRows(Path.Combine(_sourceDirectory, tableName), requestedColumns);
            _tables[tableName] = new CachedTable(new HashSet<string>(requestedColumns, StringComparer.OrdinalIgnoreCase), rows);
            return rows;
        }
    }

    private sealed record CachedTable(
        HashSet<string> Columns,
        IReadOnlyList<Dictionary<string, object?>> Rows);

    private sealed record ExportExecution(string Label, string FileName, int RecordCount, bool FileCreated);

    private sealed record InventorySourceRow(
        int Sku,
        string Name,
        decimal Pack,
        string TypeName,
        string Sname,
        string Depos,
        string Memo,
        string Cat,
        decimal? FsFactor);

    private sealed record StockSourceRow(
        int Sku,
        int Store,
        string Pvend,
        string Lvend,
        string Status,
        decimal Floor,
        decimal Back,
        decimal Acost,
        decimal Lcost,
        decimal? MinCost);

    private sealed record PriceSourceRow(
        int Sku,
        int Store,
        decimal Quantity,
        decimal Price,
        string Level,
        bool OnSale,
        decimal Sale,
        string DiscountCode,
        int Sequence);

    private sealed record UpcSourceRow(
        int Sku,
        string Upc,
        string Level,
        long LastSortValue);

    private sealed record UpcLevelEntry(int Sku, string Upc, string Level);

    private sealed record SelectedQuantityRow(string Level, decimal Quantity);

    private sealed record UpcQuantityLink(string Upc, string Level, decimal Quantity);

    private sealed record UpcQuantityLinkSummary(
        IReadOnlyList<UpcQuantityLink> Links,
        IReadOnlyList<UpcModifierLinkAuditRow> AuditRows);

    private sealed record UpcModifierLinkAuditRow(
        int Sku,
        string UpcCodes,
        string UpcLevel,
        string PrcLevel,
        string Issue,
        string Memo);

    private sealed record UpcModifierLinkAuditReportRow(
        UpcModifierLinkAuditRow AuditRow,
        string MemoId);

    private sealed record PriceEntry(PriceSourceRow Row, int Sequence);

    private sealed record PriceIndex(
        IReadOnlyDictionary<int, List<PricingRow>> PricingBySku,
        IReadOnlyDictionary<int, decimal> InventoryQuantityDivisorBySku,
        IReadOnlyDictionary<int, decimal> EffectivePackageQuantityBySku,
        IReadOnlyDictionary<int, bool> NonDiscountableBySku);

    private sealed record PricingRow(int Sku, decimal Quantity, decimal Price, string Level, int Sequence);

    private sealed record PricingSummary(
        int DefaultQuantity,
        decimal DefaultPrice,
        string ModifierQuantities,
        string ModifierCosts,
        string ModifierLastCosts,
        string ModifierPrices);

    private sealed record StockAggregate(
        string Pvend,
        string Lvend,
        string Status,
        decimal Floor,
        decimal Back,
        decimal Acost,
        decimal Lcost,
        decimal? MinCost);

    private sealed record UnitSummary(string Size, string Type);
}
