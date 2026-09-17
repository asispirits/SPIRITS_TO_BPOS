# UPC To QTY Linkage Rules

Version: `7.2.26`  
Date: `2026-07-02`

## Purpose

This document explains how the utility decides whether a UPC code can be linked to a modifier quantity in the inventory export.

The goal is to write clear UPC-to-tier relationships in BottlePOS's position-aligned modifier columns without changing the complete barcode set in `code`.

## Inventory Columns

The utility keeps the current `code` column and writes five aligned modifier columns:

| Column | Purpose |
| --- | --- |
| `code` | Existing UPC list. This remains unchanged. |
| `ModifiersQty` | Saleable tier quantities. |
| `ModifiersCost` | Cost for each tier quantity. |
| `ModifiersLatestCost` | Latest cost for each tier quantity. |
| `ModifiersPrice` | Price for each tier quantity. |
| `ModifiersStockcode` | UPC linked to each tier quantity. |

## How To Read The Link

Values in every `Modifiers*` column line up by position.

For example:

| ModifiersQty | ModifiersPrice | ModifiersStockcode |
| --- | --- | --- |
| `6,12` | `8.99,15.99` | `062067051623,062067051630` |

This means `062067051623` belongs to the qty-6 tier and `062067051630` belongs to the qty-12 tier.

## Linkable UPC Rule

A UPC is linkable only when the utility can prove a one-to-one relationship.

All of these must be true:
- `UPC.DBF.SKU` matches `PRC.DBF.SKU`.
- The UPC is numeric.
- `UPC.DBF.LEVEL` is not blank.
- `UPC.DBF.LEVEL` matches one selected `PRC.DBF.LEVEL`.
- The matched PRC level resolves to one selected `PRC.DBF.QTY`.
- No other UPC for the same SKU links to the same quantity.

When all rules pass, the UPC is written to the `ModifiersStockcode` slot matching its `ModifiersQty` quantity.

## Ordering Rule

Tier values are ordered by quantity, not by UPC code. Duplicate PRC quantities use the lowest price while every modifier list stays aligned.

## Duplicate Quantity Rule

If more than one UPC links to the same quantity for one SKU, the utility treats those UPC codes as unlinkable.

This means the tier remains in `ModifiersQty`, its `ModifiersStockcode` slot is blank, the UPC codes remain in `code`, and the UPC codes are listed in the audit report.

This protects the import from guessing when the source data does not identify one clear UPC for one quantity.

## Audit Report Rule

The audit report includes only UPC codes that could not be linked.

The report is named:

`reference_UPCModifierLinkAudit.html`

Common audit reasons include:
- no selected PRC quantity exists for the SKU
- the UPC appears under multiple UPC levels
- the UPC level is blank
- the matching PRC level has more than one quantity
- no selected PRC level matches the UPC level
- multiple UPCs point to the same quantity

The `ISSUE` column opens the row memo. The memo is embedded in the HTML report and explains why the UPC was not added to `ModifiersStockcode`.

![UPC audit sample](assets/upc_audit_sample.png)

## Expected Result

The export should be read this way:
- `code` remains the complete UPC list used by the existing import process.
- `ModifiersQty` defines the ordered tier quantities.
- `ModifiersStockcode` contains the safe UPC link in the matching position, or a blank slot when no unique link exists.
- The audit report explains UPC codes that were not safe to link.
