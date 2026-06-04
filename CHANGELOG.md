# Changelog

All notable changes to this project should be recorded in this file.

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
