# .NET Utility

This folder contains the `.NET 8` WinForms Spirits-to-BottlePOS migration utility.

Current build:
- Version: `7.2.26`
- Desktop project: `SpiritsToBottlePOSMigrationUtility`
- Command-line project: `SpiritsToBottlePOSMigrationUtility.Runner`

## Current Features

- STANDARD mode with all options available on one screen.
- GUIDED mode that walks users through source, output, export selection, inventory options, and run confirmation.
- Source and output folder selection.
- User-local saved output folder.
- Export options for departments, vendors, customers, inventory, gift cards, inactive items, sale prices, and default price levels.
- `ADD QTY=1 IF MISSING` off by default.
- Managed DBF reading without local FoxPro/dBase driver requirements.
- Validation for required DBF inputs based on selected exports.
- BottlePOS CSV generation for all selected data sets.
- `CodeToQTY` and `LinkedQTY` inventory columns for UPC-to-QTY links.
- `reference_UPCModifierLinkAudit.html` for unlinkable UPC codes.
- ZIP packaging of generated files.
- Cleanup of the temporary unpacked CSV folder.
- Final completion report with `Open ZIP Folder` and `FINISH` buttons.

## Solution Layout

- `SpiritsToBottlePOSMigrationUtility.sln`: solution file
- `SpiritsToBottlePOSMigrationUtility/Program.cs`: WinForms entry point
- `SpiritsToBottlePOSMigrationUtility/Form1.cs`: main UI behavior and layout
- `SpiritsToBottlePOSMigrationUtility/Models/`: migration request/result/progress models
- `SpiritsToBottlePOSMigrationUtility/Services/`: DBF loading, CSV writing, migration logic, export catalog, and user preferences
- `SpiritsToBottlePOSMigrationUtility.Runner/`: command-line runner

## Build

```powershell
dotnet build .\dotnet\SpiritsToBottlePOSMigrationUtility.sln -c Release
```

## Publish

```powershell
dotnet publish .\dotnet\SpiritsToBottlePOSMigrationUtility\SpiritsToBottlePOSMigrationUtility.csproj -c Release -r win-x64 --self-contained true -o .\dotnet\publish /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true
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
