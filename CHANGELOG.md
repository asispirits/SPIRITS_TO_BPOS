# Changelog

All notable changes to this project should be recorded in this file.

## 7.2.26 - 2026-07-02

- updated the .NET utility version metadata to `7.2.26`
- added `CodeToQTY` and `LinkedQTY` to `4_inventory.csv` immediately after the existing `code` column
- kept the existing `code` column unchanged so current BottlePOS import behavior remains intact
- populated `CodeToQTY` and `LinkedQTY` only when the utility can prove a one-to-one UPC-to-quantity match
- ordered linked UPC codes by the numerical order of their linked quantity
- treated multiple UPC codes for the same linked quantity as unlinkable
- added UPC-to-QTY audit output for unlinkable UPC codes only
- changed the UPC audit output to `reference_UPCModifierLinkAudit.html`
- embedded memo-style issue details directly inside the audit HTML report
- removed the audit `SUGGESTED_ACTION` output from the current report design
- set `ADD QTY=1 IF MISSING` to off by default in standard mode, guided mode, and the runner
- added an `Open ZIP Folder` option to the completion dialog
- improved ZIP creation so a temporary archive is created first and then moved into place when complete
- improved command-line runner error handling for locked files, permission issues, and unexpected failures
- reduced redundant DBF reads for department and vendor support data
- kept sale pricing out of `4_inventory.csv`; sale pricing is exported only in `reference_SalePrices.csv`
- updated `README.md`, the CSV data dictionary, and generated rule documents for the current export behavior

## 6.4.26 - 2026-06-04

- updated the .NET utility version metadata to `6.4.26`
- added STANDARD mode for the existing one-screen workflow
- added GUIDED mode to walk users through data source, export location, export selection, inventory options, and run confirmation
- added user-local output folder persistence so the previous export folder is restored on launch
- added a final completion report popup with a single `FINISH` button that closes the program
- added an `Open ZIP Folder` action after successful generation
- changed the UI title/version display to read from assembly metadata instead of hardcoded text
- fixed default price level selection so the selected level is actually used during inventory pricing
- added validation for taxable inventory when `CNT.CUSTAX` / `TXC.DBF` tax setup is missing
- set inventory `taxname` to `NoTax` when the item's category has `CAT.TAXLEVEL = 0`
- kept `taxrate` blank for `NoTax` inventory rows
- fixed notes cleanup so repeated spaces and line breaks do not concatenate words
- filtered blank vendor item numbers from UPC-derived values
- improved temporary CSV folder cleanup after failures or cancellation
- reduced repeated DBF and price-row processing during inventory export
- added `UserPreferencesService` for user-local settings
- verified Release build and self-contained publish for the updated executable

## .NET Preview Scaffold - 2026-04-02

- added a new `.NET 8` WinForms solution under `dotnet/SpiritsToBottlePOSMigrationUtility.sln`
- created the first desktop preview UI for source/output folder selection, export options, status, and planned output review
- added a command-line preview runner for repeatable validation/testing against real Spirits data folders
- added migration request/result/progress models to support the rewrite cleanly
- added a migration service that validates selected exports, checks required DBF tables, and plans output filenames/folders
- mirrored current FoxPro inventory planning rules in the preview, including `reference_SalePrices.csv` and always-generated `reference_InactiveItems.csv`
- verified the new .NET solution builds successfully
- verified the preview logic against a real store dataset and confirmed gift-card export validation fails only because `GIFTCARD.DBF` is absent from that source
- updated project documentation so the FoxPro utility and .NET rewrite are tracked together

## .NET Export Enablement - 2026-04-02

- embedded a managed DBF reader into the .NET build so export support does not depend on local FoxPro/dBase drivers
- ported department, vendor, customer, inventory, inactive-item, sale-price, and gift-card CSV generation into the .NET migration service
- preserved the current FoxPro inactive-item rule so `reference_InactiveItems.csv` is always created when inventory is selected
- preserved the sale-price rule so `reference_SalePrices.csv` contains only `SKU`, `SALE_PRICE`, and `REGULAR_PRICE`
- added ZIP archive creation for the generated .NET output folder while keeping the CSV folder on disk
- updated the WinForms UI so the main action now generates files instead of presenting itself as preview-only
- validated the .NET generator against the real `D:\BottllePOS\SPIRITS DATA\5533_2026-04-02\Data` dataset

## 4.2.26 - 2026-04-02

- updated the main window title to `Spirits to BottlePOS Migration Utility 4.2.26`
- removed the old date-based caption text from the title bar
- added `reference_InactiveItems.csv`, which is always generated for inactive items regardless of the UI checkbox state
- changed the inventory export flow so `4_inventory.csv` still respects the `Include Inactive Products` option while inactive items are also captured in their own reference export
- added inactive-item record counts to the process summary
- removed the `ON_SALE` column from `reference_SalePrices.csv` so the file now contains only `SKU`, `SALE_PRICE`, and `REGULAR_PRICE`
- bundled `SetObjRf.prg` into the project/runtime so packaged EXEs do not fail with `UNABLE TO FIND PROGRAM SETOBJRF`
- added `SetObjRf.prg` to the Visual FoxPro project so builds package the helper correctly
- set the packaged build version to `4.2.26`
- updated the project/form caption and saved build metadata to version `4.2.26`
- moved the `Include Inactive Products` option to the right side of the form so it no longer crowds the `Default Price Level` option
- standardized the packaged executable name to `SpiritsToBottlePOSMigrationUtility.exe`
- added project documentation in `README.md`
- added `CHANGELOG.md` to maintain an ongoing change history for the utility
- updated the Visual FoxPro form window so it is no longer forced into a centered always-on-top dialog and can be moved/minimized normally
- verified the .NET export output matches the Visual FoxPro output exactly for departments, vendors, customers, inventory, inactive items, and sale prices using the provided `5533_2026-04-02\Data` snapshot
- changed the .NET export flow so the generated CSV folder is zipped and then removed, leaving the ZIP archive as the final output
