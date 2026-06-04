# Spirits to BottlePOS Migration Utility

This repository contains the Visual FoxPro source/assets and packaged binary for the Spirits-to-BottlePOS migration utility, along with the early .NET rewrite scaffold under `dotnet/`.

The current runtime build in source is **4.2.26**, and the canonical packaged executable name is:
- `SpiritsToBottlePOSMigrationUtility.exe`

## Recent changes in 4.2.26

- renamed the utility in the title bar to `Spirits to BottlePOS Migration Utility 4.2.26`
- removed the old date-stamped title text
- added `reference_InactiveItems.csv` as an always-generated inactive-item reference export
- kept `4_inventory.csv` controlled by the `Include Inactive Products` option
- removed the `ON_SALE` column from `reference_SalePrices.csv`
- bundled `SetObjRf.prg` with the project/runtime to prevent packaged EXE startup failures
- moved the `Include Inactive Products` option farther right in the UI so it no longer crowds the default price-level option
- updated packaged build/version metadata to `4.2.26`
- standardized the packaged executable name to `SpiritsToBottlePOSMigrationUtility.exe`
- added `CHANGELOG.md` so project changes can be tracked over time
- updated the Visual FoxPro window so it opens as a normal movable/minimizable desktop window instead of a centered locked dialog
- started the `.NET 8` WinForms rewrite with a buildable solution under `dotnet/`
- added embedded DBF reading to the .NET build so it can run without machine-specific FoxPro/DBF drivers
- enabled real CSV generation and ZIP packaging in the .NET build
- verified the .NET CSV output matches the FoxPro CSV output exactly for the provided `5533_2026-04-02\Data` comparison dataset, excluding gift cards because `GIFTCARD.DBF` is not present

## What the application does

Based on the current code (`main.prg`, `fcoversion.scx`, `fcoversion.sct`), the migration utility:
- prompts for a Spirits/KSV data folder and an output folder
- exports BottlePOS import/reference CSVs for departments, vendors, customers, inventory, sale prices, gift cards, and inactive inventory items
- respects the `Include Inactive Products` option for the main `4_inventory.csv`
- always produces `reference_InactiveItems.csv` for items outside active inventory status codes
- filters sale-price exports to `prc.onsale = .T.` records and writes only `SKU`, `SALE_PRICE`, and `REGULAR_PRICE`
- creates a timestamped output folder, packages it into a ZIP archive, and removes the temporary CSV folder when processing completes

## Generated files

When the corresponding options are selected, the utility writes these CSV files into the output folder:
- `1_departments.csv`
- `2_vendors.csv`
- `3_customers.csv`
- `4_inventory.csv`
- `5_gift_cards.csv`
- `reference_SalePrices.csv`
- `reference_InactiveItems.csv`

## Repository layout

- `main.prg`: startup program that launches the migration form
- `fcoversion.scx` / `fcoversion.sct`: main Visual FoxPro form and form code
- `BottlePOS.pjx` / `BottlePOS.pjt`: Visual FoxPro project files
- `ClassLibs/`: shared class libraries used by the form
- `SetObjRf.prg`: bundled FoxPro helper required by the packaged runtime
- `README.md` / `CHANGELOG.md`: project overview and change history
- `2000.ico`: application icon
- `dotnet/`: .NET 8 WinForms rewrite scaffold and supporting code

## .NET build

The new .NET work lives in:
- `dotnet/SpiritsToBottlePOSMigrationUtility.sln`
- `dotnet/SpiritsToBottlePOSMigrationUtility/`

Current .NET scope:
- WinForms desktop utility for the migration workflow
- command-line runner for repeatable validation and generation work
- source and output folder selection
- export option parity for departments, vendors, customers, inventory, gift cards, inactive handling, and default price level
- embedded DBF reading with bundled package dependencies instead of relying on local driver installs
- validation of required DBF files for selected exports
- CSV generation that mirrors current FoxPro filenames, including `reference_SalePrices.csv` and `reference_InactiveItems.csv`
- ZIP creation for the generated output folder, followed by cleanup of the temporary unpacked CSV folder
- summary/status messaging for validation and generation runs

Current .NET limitations:
- output parity has been verified against the Visual FoxPro build for `1_departments.csv`, `2_vendors.csv`, `3_customers.csv`, `4_inventory.csv`, `reference_SalePrices.csv`, and `reference_InactiveItems.csv` using the provided `D:\BottllePOS\SPIRITS DATA\5533_2026-04-02\Data` snapshot
- the provided `5533_2026-04-02` test dataset does not contain `GIFTCARD.DBF`, so gift-card export must be unchecked for that source snapshot

## Input expectations

The utility expects a Spirits/KSV data directory that contains the DBF tables used by the selected exports. Current code checks for:
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
- `DEP.DBF`
- `VND.DBF`

If required tables are missing, the application warns the user before processing.

## Running the packaged migration utility (Windows)

1. Launch `SpiritsToBottlePOSMigrationUtility.exe`.
2. Select the Spirits/KSV data directory.
3. Select the output directory.
4. Choose which data sets to export.
5. Click the process button to generate the ZIP archive that contains the CSVs.

## Working with source

1. Open `BottlePOS.pjx` in Visual FoxPro 9.
2. Main startup program is `main.prg`.
3. The primary export logic lives in `fcoversion.scx` / `fcoversion.sct`.
4. Runtime builds should include `SetObjRf.prg` so packaged executables can resolve the shared `_base.vcx` class behavior.
5. The .NET rewrite currently builds with `dotnet build dotnet/SpiritsToBottlePOSMigrationUtility.sln`.
6. The .NET runner can validate or generate from the command line:
   `dotnet run --project .\dotnet\SpiritsToBottlePOSMigrationUtility.Runner -- --source "D:\path\to\Data" --output "D:\path\to\Output"`
7. The current self-contained .NET desktop test build is published to `dotnet\publish\SpiritsToBottlePOSMigrationUtility.exe`.

## Notes

- There are no automated tests in this repository snapshot.
- This repository tracks source and packaged binaries together.
- The current UI caption is `Spirits to BottlePOS Migration Utility 4.2.26`.
- The current Visual FoxPro desktop window is configured to be movable and minimizable.
