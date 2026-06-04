using System.Globalization;
using System.IO.Compression;
using System.Text;
using SpiritsToBottlePOSMigrationUtility.Models;

namespace SpiritsToBottlePOSMigrationUtility.Services;

public sealed class MigrationService : IMigrationService
{
    private readonly DbfTableLoader _dbfTableLoader = new();
    private readonly CsvFileWriter _csvFileWriter = new();

    private static readonly string[] InventoryHeader = new[]
    {
        "ImportType",
        "code",
        "sku",
        "name",
        "cost",
        "lastcost",
        "price",
        "qty",
        "unitspercase",
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
                progress?.Report(new MigrationProgress(78, "Generating inventory, inactive items, and sale prices..."));
                exportResults.AddRange(await ExportInventoryBundleAsync(rowCache, plannedOutputDirectory, request.Options, cancellationToken));
            }

            if (request.Options.ExportGiftCards)
            {
                progress?.Report(new MigrationProgress(86, "Generating gift cards..."));
                exportResults.Add(await ExportGiftCardsAsync(rowCache, plannedOutputDirectory, cancellationToken));
            }

            progress?.Report(new MigrationProgress(94, "Creating ZIP archive..."));
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(zipFilePath))
            {
                File.Delete(zipFilePath);
            }

            ZipFile.CreateFromDirectory(plannedOutputDirectory, zipFilePath, CompressionLevel.Optimal, includeBaseDirectory: false);

            progress?.Report(new MigrationProgress(97, "Removing temporary CSV folder..."));
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
        var txcRows = rowCache.ReadRows("TXC.DBF", "CODE", "RATE", "DESCRIPT");
        var vendorRows = rowCache.ReadRows("VND.DBF", "VCODE", "LASTNAME", "FIRSTNAME");
        var categoryRows = rowCache.ReadRows("CAT.DBF", "CAT", "NAME", "TAXLEVEL");
        var inventoryRows = rowCache.ReadRows("INV.DBF", "SKU", "NAME", "PACK", "TYPENAME", "SNAME", "DEPOS", "MEMO", "CAT")
            .Select(ToInventorySourceRow)
            .ToList();
        var stockRows = rowCache.ReadRows("STK.DBF", "SKU", "STORE", "PVEND", "LVEND", "STAT", "FLOOR", "BACK", "ACOST", "LCOST")
            .Select(ToStockSourceRow)
            .ToList();
        var priceRows = rowCache.ReadRows("PRC.DBF", "SKU", "STORE", "QTY", "PRICE", "LEVEL", "ONSALE", "SALE")
            .Select((row, index) => ToPriceSourceRow(row, index))
            .ToList();
        var upcRows = rowCache.ReadRows("UPC.DBF", "UPC", "SKU", "LAST")
            .Select(ToUpcSourceRow)
            .ToList();

        var saleTaxCodeRow = cntRows.FirstOrDefault(row =>
            string.Equals(UpperTrim(GetString(row, "CODE")), "CUSTAX", StringComparison.OrdinalIgnoreCase));
        var saleTaxCode = UpperTrim(saleTaxCodeRow is null ? string.Empty : GetString(saleTaxCodeRow, "DATA"));

        var saleTaxRate = 0m;
        var saleTaxName = string.Empty;
        if (!string.IsNullOrWhiteSpace(saleTaxCode))
        {
            var taxRow = txcRows.FirstOrDefault(row =>
                string.Equals(UpperTrim(GetString(row, "CODE")), saleTaxCode, StringComparison.OrdinalIgnoreCase));

            if (taxRow is not null)
            {
                saleTaxRate = VfpRound(ToDecimal(taxRow, "RATE") * 100m, 2);
                saleTaxName = CleanUpperText(
                    string.IsNullOrWhiteSpace(GetString(taxRow, "DESCRIPT"))
                        ? GetString(taxRow, "CODE")
                        : GetString(taxRow, "DESCRIPT"));
            }
        }

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

        var hasTaxableInventory = inventoryRows.Any(row =>
            categoryTaxLevelByCode.TryGetValue(UpperTrim(row.Cat), out var level) && level > 0m);

        if (hasTaxableInventory && string.IsNullOrWhiteSpace(saleTaxName))
        {
            throw new InvalidOperationException(
                "Inventory includes taxable items, but the sale tax setup could not be found in CNT.CUSTAX/TXC.DBF. Fix the tax setup or exclude inventory before generating output.");
        }

        var stockBySku = stockRows
            .Where(row => row.Store == store)
            .GroupBy(row => row.Sku)
            .ToDictionary(group => group.Key, BuildStockAggregate);

        var priceIndex = BuildPriceIndex(priceRows, store, options);
        var upcBySku = BuildUpcBySku(upcRows);
        var vendorItemBySku = BuildVendorItemNumbersBySku(upcRows);

        var inventoryRowsForCsv = new List<IReadOnlyList<string>> { InventoryHeader };
        var inactiveRowsForCsv = new List<IReadOnlyList<string>> { InventoryHeader };

        foreach (var inventoryRow in inventoryRows.OrderBy(row => row.Sku))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sku = inventoryRow.Sku;
            stockBySku.TryGetValue(sku, out var stock);
            priceIndex.PricingBySku.TryGetValue(sku, out var pricingRows);
            upcBySku.TryGetValue(sku, out var productUpcs);
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
            var taxName = isTaxable
                ? saleTaxName
                : hasCategoryTaxLevel && taxLevel == 0m
                    ? "NoTax"
                    : string.Empty;

            var outputRow = new[]
            {
                "I",
                TrimToLength(RemoveCharacters(productUpcs is null ? string.Empty : string.Join(",", productUpcs), "\"-+@"), 250),
                FormatSku(sku),
                TrimToLength(CleanUpperMultilineText(inventoryRow.Name), 100),
                TrimToLength(FormatMoney(averageCostPerUnit * pricingSummary.DefaultQuantity), 25),
                TrimToLength(FormatMoney(lastCostPerUnit * pricingSummary.DefaultQuantity), 50),
                TrimToLength(FormatMoney(pricingSummary.DefaultPrice), 25),
                FormatQuantity(quantityValue),
                TrimToLength(FormatInteger(unitsPerCaseValue), 25),
                TrimToLength(taxName, 30),
                TrimToLength(isTaxable ? FormatRate(saleTaxRate) : string.Empty, 30),
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

        return new[] { inventoryResult, inactiveResult, salePriceResult };
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
        var categoryRows = rowCache.ReadRows("CAT.DBF", "CAT", "NAME");
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
        CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(outputDirectory, fileName);
        var shouldWrite = writeWhenHeaderOnly || rows.Count > 1;

        if (shouldWrite)
        {
            await _csvFileWriter.WriteAsync(filePath, rows, cancellationToken);
        }

        return new ExportExecution(label, fileName, Math.Max(rows.Count - 1, 0), shouldWrite);
    }

    private static PriceIndex BuildPriceIndex(
        IReadOnlyList<PriceSourceRow> priceRows,
        int store,
        ExportOptions options)
    {
        var pricingBySku = new Dictionary<int, List<PricingRow>>();
        var effectivePackageQuantityBySku = new Dictionary<int, decimal>();
        var inventoryQuantityDivisorBySku = new Dictionary<int, decimal>();

        foreach (var group in BuildQualifiedPriceEntries(priceRows, options).GroupBy(entry => entry.Row.Sku))
        {
            var pricedEntries = GetPreferredStoreEntries(group, store, entry => entry.Row.Price > 0m);
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
        }

        return new PriceIndex(pricingBySku, inventoryQuantityDivisorBySku, effectivePackageQuantityBySku);
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
            group.Max(row => row.Lcost));
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
            builder.AppendLine("- reference_InactiveItems.csv is always generated when inventory is selected.");
            builder.AppendLine("- reference_SalePrices.csv contains SKU, SALE_PRICE, and REGULAR_PRICE.");
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
                $"- {exportResult.Label}: {exportResult.RecordCount} record(s){(exportResult.FileCreated ? $" -> {exportResult.FileName}" : " -> no CSV written")}");
        }

        if (request.Options.ExportInventory)
        {
            builder.AppendLine();
            builder.AppendLine("Inventory behavior:");
            builder.AppendLine($"- Main inventory {(request.Options.IncludeInactiveProducts ? "included" : "excluded")} inactive items.");
            builder.AppendLine($"- Missing QTY=1 rows were {(request.Options.AddQuantityOneIfMissing ? "added when needed" : "left unchanged")}.");
            builder.AppendLine("- bottledeposit used the smallest effective quantity followed by PK when an item had a deposit code.");
            builder.AppendLine("- Inactive items were also written to reference_InactiveItems.csv.");
            builder.AppendLine("- Sale prices were written without the ON_SALE column.");
        }

        builder.AppendLine();
        builder.AppendLine($"Temporary CSV folder: {outputDirectory}");
        builder.AppendLine("The CSV files were packaged into the ZIP archive and the temporary folder was removed.");

        return builder.ToString().TrimEnd();
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
            GetString(row, "CAT"));
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
            ToDecimal(row, "LCOST"));
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
            sequence);
    }

    private static UpcSourceRow ToUpcSourceRow(IReadOnlyDictionary<string, object?> row)
    {
        return new UpcSourceRow(
            ToInt32(row, "SKU"),
            GetString(row, "UPC"),
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

    private static string FormatRate(decimal value)
    {
        return VfpRound(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatQuantity(decimal value)
    {
        return VfpRound(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
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
        string Cat);

    private sealed record StockSourceRow(
        int Sku,
        int Store,
        string Pvend,
        string Lvend,
        string Status,
        decimal Floor,
        decimal Back,
        decimal Acost,
        decimal Lcost);

    private sealed record PriceSourceRow(
        int Sku,
        int Store,
        decimal Quantity,
        decimal Price,
        string Level,
        bool OnSale,
        decimal Sale,
        int Sequence);

    private sealed record UpcSourceRow(
        int Sku,
        string Upc,
        long LastSortValue);

    private sealed record PriceEntry(PriceSourceRow Row, int Sequence);

    private sealed record PriceIndex(
        IReadOnlyDictionary<int, List<PricingRow>> PricingBySku,
        IReadOnlyDictionary<int, decimal> InventoryQuantityDivisorBySku,
        IReadOnlyDictionary<int, decimal> EffectivePackageQuantityBySku);

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
        decimal Lcost);

    private sealed record UnitSummary(string Size, string Type);
}
