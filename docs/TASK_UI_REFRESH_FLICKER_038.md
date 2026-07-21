# TASK-UI-REFRESH-FLICKER-038

## Scope

- Target version: `8.10.10`.
- Fix only the main-window refresh presentation path.
- Preserve the random 2-4 second local refresh schedule.
- Do not change daily PnL, broker holding PnL, TradeLog, strategy, order-draft, market-source, account, or database semantics.

## Root cause

`RefreshLocalDataAndUi` previously rebuilt the ETF table, TradeLog grid, order-draft grid, sparklines, drawdown charts, ring, and pool on every timer tick. Account replay, strategy calculation, and order-draft completion could each request another complete refresh in the same dispatcher period. The outer `Viewbox` amplified the visual impact of these repeated clear-and-rebuild operations.

## Locked implementation

1. `MainWindowUiRefreshCoordinator` merges dirty flags in one dispatcher cycle.
2. Background account replay, strategy, and order-draft callbacks request only affected surfaces; they do not directly call a full refresh.
3. ETF cells are indexed by `strategyCode + columnKey` and retain the same `Border` and `TextBlock` instances during ordinary quote updates.
4. ETF structure is rebuilt only when the visible column structure, display row order, pin configuration, or header drag structure changes.
5. TradeLog and order-draft grids rebuild only when their signatures change.
6. Sparklines and drawdown charts redraw only when their input snapshot/data signature or required display size changes.
7. The ring keeps a persistent path and changes only its geometry/visibility when completion changes.
8. The static pool graphic is created once.
9. Existing ETF value-change animation applies to retained cells and is not implemented through control destruction/recreation.
10. No full-page hiding, opacity fade, sleep, delayed display, software rendering, screenshot overlay, layout ratio change, or manual refresh button is introduced.

## Logical-cycle diagnostics

The pre-fix source path deterministically performed one full visual rebuild per unchanged timer cycle: 100 logical cycles meant 100 ETF builds, 100 TradeLog builds, 100 order-draft builds, 100 drawdown rebuild attempts, and 100 redraw attempts for other canvases. Background completions could add duplicate complete refreshes.

The coordinator regression runs 100 unchanged logical cycles. It records 100 refresh cycles, one initial render for a stable surface, and 99 skipped unchanged renders. This is a deterministic non-production diagnostic and does not write `runtime_log`. Actual UI-thread duration, visual-object allocation, and Gen0/Gen1 counts require the final manual runtime acceptance because automatically starting the full executable would use the user's application environment.

## Verification

- Stable ETF references and property-only updates.
- One-cell value update path retains animation.
- Unchanged TradeLog and order-draft signatures skip grid rebuilds.
- Unchanged chart signatures skip canvas redraws.
- Multiple dirty requests coalesce into one dispatcher cycle.
- Structural ETF changes still permit rebuilding.
- Random `2000..4000 ms` scheduling remains unchanged.
