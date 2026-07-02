# Spirits to BottlePOS Migration Utility

This repository contains the current `.NET 8` Spirits-to-BottlePOS migration utility source.

Current build:
- Version: `7.2.26`
- Application: `SpiritsToBottlePOSMigrationUtility.exe`
- Platform: Windows desktop, self-contained .NET publish supported

## What The Utility Does

The utility converts Spirits/KSV DBF data into BottlePOS-ready CSV import files and reference files.

When selected, it can export:
- departments
- vendors
- customers
- inventory
- gift cards
- sale-price reference data
- inactive-item reference data
- UPC-to-QTY link audit data

Generated CSV and reference files are packaged into a timestamped ZIP archive. The temporary unpacked CSV folder is removed after the ZIP is created. After a successful run, the UI offers an `Open ZIP Folder` action so the finished archive can be opened quickly.

## 7.2.26 Highlights

- Added `CodeToQTY` and `LinkedQTY` to `4_inventory.csv` immediately after the existing `code` column.
- Kept the existing `code` column unchanged so current import behavior remains intact.
- Populated `CodeToQTY` and `LinkedQTY` only when one UPC can be safely linked to one selected quantity.
- Sorted linked UPCs by the numerical order of their linked quantity.
- Treated duplicate quantity matches as unlinkable. If two UPC codes point to the same quantity, neither UPC is added to `CodeToQTY`.
- Replaced the UPC audit CSV with `reference_UPCModifierLinkAudit.html`.
- Kept only unlinkable UPC codes in the audit report.
- Added memo-style issue links in the audit `ISSUE` column. The memo content is embedded in the HTML report.
- Set `ADD QTY=1 IF MISSING` to off by default in both standard and guided mode.
- Added safer ZIP creation by writing to a temporary archive first and moving it into place only after the ZIP is complete.
- Improved runner error handling for locked files, permission issues, and unexpected failures.
- Kept sale pricing out of `4_inventory.csv`. Sale pricing is exported only in `reference_SalePrices.csv`.

## Required Source Tables

The selected exports determine which DBF files are required. Depending on options, the utility may require:
- `CAT.DBF`
- `TYP.DBF`
- `CUS.DBF`
- `GIFTCARD.DBF`
- `INV.DBF`
- `STR.DBF`
- `STK.DBF`
- `PRC.DBF`
- `UPC.DBF`
- `DSC.DBF`
- `CNT.DBF`
- `TXC.DBF`
- `VND.DBF`

If required tables are missing, the application reports the issue before processing.

## Output Files

Common output files include:
- `1_departments.csv`
- `2_vendors.csv`
- `3_customers.csv`
- `4_inventory.csv`
- `reference_InactiveItems.csv`
- `reference_SalePrices.csv`
- `reference_UPCModifierLinkAudit.html`
- `5_gift_cards.csv`

Inventory output includes the original `code` column plus the new `CodeToQTY` and `LinkedQTY` columns. `CodeToQTY` contains linkable UPC codes. `LinkedQTY` contains the matching quantities in the same order.

## Running The Application

1. Launch `SpiritsToBottlePOSMigrationUtility.exe`.
2. Choose `STANDARD` or `GUIDED` mode.
3. Select the Spirits/KSV data directory.
4. Select the output directory.
5. Choose which data sets to export.
6. If exporting inventory, choose inventory options.
7. Run the migration.
8. Review the completion report.
9. Use `Open ZIP Folder` if you want to open the finished archive location.
10. Select `FINISH`.

## Repository Layout

- `dotnet/SpiritsToBottlePOSMigrationUtility.sln`: Visual Studio solution
- `dotnet/SpiritsToBottlePOSMigrationUtility/`: WinForms desktop app
- `dotnet/SpiritsToBottlePOSMigrationUtility.Runner/`: command-line runner
- `dotnet/SpiritsToBottlePOSMigrationUtility/Models/`: migration request/result/progress models
- `dotnet/SpiritsToBottlePOSMigrationUtility/Services/`: DBF loading, CSV export, migration, catalog, and preferences services
- `docs/`: export rule documents and Word handoff files
- `2000.ico`: application icon
- `CHANGELOG.md`: project history
- `CSV_DATA_DICTIONARY.md`: CSV field mapping reference

## Build

```powershell
dotnet build .\dotnet\SpiritsToBottlePOSMigrationUtility.sln -c Release
```

## Publish A Windows EXE

```powershell
dotnet publish .\dotnet\SpiritsToBottlePOSMigrationUtility\SpiritsToBottlePOSMigrationUtility.csproj -c Release -r win-x64 --self-contained true -o .\dotnet\publish /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true
```

The published executable will be:

```text
dotnet\publish\SpiritsToBottlePOSMigrationUtility.exe
```

## Runner Usage

```powershell
dotnet run --project .\dotnet\SpiritsToBottlePOSMigrationUtility.Runner -- --source "D:\path\to\Data" --output "D:\path\to\Output"
```

Optional runner flags include:
- `--departments false`
- `--vendors false`
- `--customers false`
- `--inventory false`
- `--giftcards false`
- `--includeinactive true`
- `--addqty1ifmissing false`
- `--usedefaultpricelevel true`
- `--pricelevel 1`
- `--preview true`

## Documentation

- `CSV_DATA_DICTIONARY.md` and `CSV_DATA_DICTIONARY.docx` describe generated CSV fields.
- `docs/SpiritsToBottlePOS_Export_Rules.md` and `.docx` describe the full export rule set.
- `docs/UPC_To_QTY_Linkage_Rules.md` and `.docx` describe only the UPC-to-QTY linking rules.

## Notes

- The project uses managed DBF reading and does not require local FoxPro/dBase drivers.
- The last output directory is stored under the current Windows user's local application data.
- Generated build outputs are intentionally ignored by source control.
