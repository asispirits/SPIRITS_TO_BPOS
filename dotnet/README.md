# .NET Rewrite

This folder contains the `.NET 8` WinForms rewrite of the Spirits to BottlePOS migration utility.

## Current status

The current build includes:
- a desktop form for source and output folder selection
- a command-line runner for headless validation or generation
- export options that mirror the FoxPro workflow
- embedded DBF reading so the app does not depend on local FoxPro/dBase drivers
- validation for required DBF inputs based on the selected exports
- BottlePOS CSV generation for departments, vendors, customers, inventory, sale prices, inactive items, and gift cards
- ZIP packaging of the generated output folder, followed by removal of the temporary unpacked CSV folder
- current inventory planning rules for `reference_SalePrices.csv` and `reference_InactiveItems.csv`

Current cautions:
- the real-data export path is working, but we still want broader FoxPro-to-.NET comparison before calling the rewrite final
- the provided `5533_2026-04-02` source snapshot does not include `GIFTCARD.DBF`, so gift cards must be unchecked for that dataset

## Solution layout

- `SpiritsToBottlePOSMigrationUtility.sln`: solution file
- `SpiritsToBottlePOSMigrationUtility/Program.cs`: WinForms entry point
- `SpiritsToBottlePOSMigrationUtility/Form1.cs`: main form behavior and layout
- `SpiritsToBottlePOSMigrationUtility/Models/`: migration request/result/progress models
- `SpiritsToBottlePOSMigrationUtility/Services/`: validation and output-planning services
- `SpiritsToBottlePOSMigrationUtility.Runner/`: command-line runner for repeatable source validation and generation

## Build

```powershell
dotnet build .\dotnet\SpiritsToBottlePOSMigrationUtility.sln
```

## Runner usage

```powershell
dotnet run --project .\dotnet\SpiritsToBottlePOSMigrationUtility.Runner -- --source "D:\path\to\Data" --output "D:\path\to\Output"
```

## Next porting targets

- compare generated CSV output against the FoxPro utility across more store datasets
- tighten UI behavior around unavailable source tables so missing exports are clearer before the run starts
- decide when the `.NET` build is ready to replace the FoxPro build as the primary executable
