# TASK-CLEAN-CHECKOUT-TEST-PORTABILITY-032

## Purpose

V8.10.2 is a test-portability patch. It keeps the accepted V8.10.1 TradeLog atomic-save production implementation unchanged and removes operating-system line-ending sensitivity from two source-structure tests.

## V8.10.1 release history

- V8.10.1 completed its production-code commit, lock commit, and pushed annotated tag.
- The pushed `v8.10.1` tag is immutable and remains pointed at `3cbb1b194e93bb6f226e930e5c5945d300dde16f`.
- V8.10.1 was not formally published.
- The main working tree passed `1675/1675`, while the final clean detached checkout passed `1673/1675`.
- The two clean-checkout failures were:
  - `MarketMonitorWindowTests.SourceGrid_HasReadableHeightTypographyAndFullErrorToolTip`
  - `SystemSettingsCenterTests.WindowConstructor_KeepsRequiredBuildLoadScopeAndHealthLifecycleOrder`

## Root cause

The machine uses `core.autocrlf=true` and the repository has no `.gitattributes`. The main working tree retained LF or mixed line endings, while a clean checkout used CRLF. The two tests embedded LF-only multi-line source markers, so their text extraction failed even though the required production XAML and constructor behavior were present. No product-code functional failure was found.

## Patch scope

- Normalize source text to LF inside test-only helper code before source slicing or semantic checks.
- Preserve the Market Monitor assertions for readable grid sizing, typography, required columns, and the full error tooltip binding.
- Preserve and strengthen the System Settings constructor-order check so every required lifecycle marker must exist in the required order.
- Cover LF, CRLF, mixed LF/CRLF, and CR-only in-memory source inputs.
- Cover missing structures, reversed call order, and deleted required attributes as negative cases.
- Update assembly and display-version expectations to V8.10.2.

## Protected boundaries

- No production implementation is modified.
- No Market Monitor XAML or code-behind is modified.
- No System Settings or `ManualDataEntryWindow` production file is modified.
- No TradeLog atomic-save implementation or account, strategy, order, persistence, or database behavior is modified.
- No `.gitattributes`, editor configuration, global Git configuration, or repository-wide line ending is changed.
- V8.10.1 remains unchanged and un-released; V8.10.2 carries the accepted behavior through the clean-checkout release gate.
