# Spirits to BottlePOS Migration Utility

This repository contains the current `.NET 8` Spirits-to-BottlePOS migration utility source.

Current build:
- Version: `6.4.26`
- Application: `SpiritsToBottlePOSMigrationUtility.exe`
- Platform: Windows desktop, self-contained .NET publish supported

## What The Utility Does

The utility converts Spirits/KSV DBF data into BottlePOS-ready CSV import and reference files.

When selected, it can export:
- departments
- vendors
- customers
- inventory
- gift cards
- sale-price reference data
- inactive-item reference data

Generated CSV files are packaged into a timestamped ZIP archive. The temporary unpacked CSV folder is removed after the ZIP is created.

## 6.4.26 Highlights

- Added `STANDARD` mode for the current all-options-at-once workflow.
- Added `GUIDED` mode to walk users step by step through source selection, output selection, export choices, inventory options, and run confirmation.
- Added user-local output folder memory so the previous export folder is prefilled on the next launch.
- Added a final completion report popup with one `FINISH` button that closes the program.
- Added an `Open ZIP Folder` action after successful generation.
- Updated the UI title to read version information from assembly metadata.
- Fixed default price level selection so selected levels `1`, `2`, or `3` are honored.
- Added safer taxable-inventory validation when `CNT.CUSTAX` / `TXC.DBF` tax setup is missing.
- Added `NoTax` output for inventory items whose category has `CAT.TAXLEVEL = 0`.
- Fixed note whitespace cleanup so words are not accidentally concatenated.
- Filtered blank vendor item numbers from UPC-derived values.
- Improved temporary output cleanup after errors or cancellation.
- Reduced repeated DBF/PRC processing during inventory export.

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
- `CNT.DBF`
- `TXC.DBF`
- `VND.DBF`

If required tables are missing, the application reports the issue before processing.

## Running The Application

1. Launch `SpiritsToBottlePOSMigrationUtility.exe`.
2. Choose `STANDARD` or `GUIDED` mode.
3. Select the Spirits/KSV data directory.
4. Select the output directory.
5. Choose which data sets to export.
6. If exporting inventory, choose inventory options.
7. Run the migration.
8. Review the completion report and select `FINISH`.

## Repository Layout

- `dotnet/SpiritsToBottlePOSMigrationUtility.sln`: Visual Studio solution
- `dotnet/SpiritsToBottlePOSMigrationUtility/`: WinForms desktop app
- `dotnet/SpiritsToBottlePOSMigrationUtility.Runner/`: command-line runner
- `dotnet/SpiritsToBottlePOSMigrationUtility/Models/`: migration request/result/progress models
- `dotnet/SpiritsToBottlePOSMigrationUtility/Services/`: DBF loading, CSV export, migration, catalog, and preferences services
- `2000.ico`: application icon
- `CHANGELOG.md`: project history

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
- `--pricelevel 1`
- `--preview true`

## Notes

- The project uses managed DBF reading and does not require local FoxPro/dBase drivers.
- The last output directory is stored under the current Windows user's local application data.
- Generated build outputs are intentionally ignored by source control.
