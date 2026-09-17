# Spirits To BottlePOS Export Rules

Version: `7.2.26`  
Date: `2026-07-02`

## Purpose

This document explains the rules the utility follows when it creates BottlePOS export files from Spirits/KSV DBF data. It is intended to be a practical reference for reviewing output and setting expectations before import.

## Output Package

The utility writes selected export files to a temporary output folder, creates a timestamped ZIP archive, then removes the temporary folder. The final ZIP is the expected handoff file.

When inventory is selected, these reference files may also be included:
- `reference_InactiveItems.csv`
- `reference_SalePrices.csv`
- `reference_UPCModifierLinkAudit.html`

The UPC audit report is generated as an HTML file because it includes embedded memo details for each unlinkable UPC.

## General Formatting Rules

- Text is trimmed and generally converted to uppercase.
- Double quotes are removed from text fields.
- Line breaks inside source text are flattened to spaces.
- Money values in import CSV files use 2 decimal places.
- Sale price reference values use 4 decimal places.
- Whole-number values are truncated rather than rounded.
- SKUs are exported as text. Short SKUs are padded to the expected text format.

## Export Selection Rules

The selected checkboxes control which data sets are exported. If a selected export requires source tables that are not present, the utility reports the missing source data before processing.

| Export | Main source data |
| --- | --- |
| Departments | `CAT.DBF`, `TYP.DBF` |
| Vendors | `VND.DBF` |
| Customers | `CUS.DBF` |
| Inventory | `INV.DBF`, `STK.DBF`, `PRC.DBF`, `UPC.DBF`, tax, vendor, category, and discount support tables |
| Gift cards | `GIFTCARD.DBF` |

## Inventory Pricing Rules

Inventory pricing comes from selected `PRC.DBF` rows where `QTY > 0`.

All applicable price levels are considered except levels `7`, `8`, and `9`.

If pricing exists for the active store, active-store pricing is preferred. If no active-store pricing exists, store `1` is preferred.

The item row always represents one unit. If QTY=1 pricing exists, its lowest price is used. Otherwise the lowest-quantity price is divided by its quantity and rounded to two decimals. Distinct whole-number quantities greater than one become modifier tiers; duplicate quantities use the lowest price.

## Quantity Rules

Inventory `qty` is the shared unit stock calculated as `BACK + FLOOR`. Item cost and latest cost are unit costs, and `unitspercase` uses `INV.DBF.PACK` directly.

## Inventory Column Placement

The inventory export keeps the existing `code` column and writes `ModifiersQty`, `ModifiersCost`, `ModifiersLatestCost`, `ModifiersPrice`, and `ModifiersStockcode` as position-aligned lists.

The `size` column is also exported as a combined value such as `12 OZ` or `750 ML`; the existing `Unit_Size` and `Unit_Type` columns remain for compatibility.

## UPC-To-QTY Linking Rules

The utility uses the existing Spirits hierarchy to identify which UPC code belongs to which selected quantity. It does not export `UPC.LEVEL` or `PRC.LEVEL` in `4_inventory.csv`.

A UPC is linkable only when:
- `UPC.DBF.SKU` matches `PRC.DBF.SKU`.
- The UPC is numeric.
- `UPC.DBF.LEVEL` is not blank.
- `UPC.DBF.LEVEL` matches one selected `PRC.DBF.LEVEL`.
- That matched PRC level resolves to one selected quantity.
- No other UPC for the same SKU links to that same quantity.

When a UPC is linkable, it is added to the `ModifiersStockcode` slot matching the tier's `ModifiersQty` position. Every nonblank modifier stockcode also remains in `code`.

Each `ModifiersStockcode` lines up with the quantity, cost, latest cost, and price in the same position.

## Duplicate Quantity Protection

If two UPC codes point to the same quantity for one SKU, the utility does not choose one. The tier's `ModifiersStockcode` slot remains blank and both UPCs are audited.

This prevents an import from guessing which code should control a modifier quantity.

The UPC codes still remain in the original `code` column.

## UPC Audit Rules

The UPC audit report includes only UPC codes that could not be safely linked.

Common audit reasons include:
- no selected PRC quantity exists for the SKU
- the UPC appears under multiple UPC levels
- the UPC level is blank
- the matching PRC level has more than one quantity
- no selected PRC level matches the UPC level
- multiple UPCs point to the same quantity

The `ISSUE` column opens an embedded memo for the row. The memo explains why that UPC stayed out of `ModifiersStockcode`.

![UPC audit sample](assets/upc_audit_sample.png)

## Inactive Item Rules

Inactive status is based on the selected store's `STK.DBF.STAT` value.

- Status `2` and `8` are active.
- Other statuses are treated as inactive.
- Inactive items are always written to `reference_InactiveItems.csv`.
- Inactive items are included in `4_inventory.csv` only when `Include inactive products` is selected.

## Sale Price Rules

Sale pricing is not written to `4_inventory.csv`.

Sale pricing is exported only to `reference_SalePrices.csv`, using rows where `PRC.DBF.ONSALE` is true for the active store.

The sale price file contains:
- `SKU`
- `SALE_PRICE`
- `REGULAR_PRICE`

## ZIP And Completion Rules

The utility creates the ZIP archive through a temporary file first. The final ZIP is moved into place only after creation succeeds.

After a successful run, the completion dialog includes an `Open ZIP Folder` option.
