# TASK-DAILY-PNL-COST-BASIS-035

## Scope

- Target version: `8.10.7`.
- TradeLog remains the only transaction fact source.
- No database schema or TradeLog field changes.
- No strategy, order draft, market route, or refresh-frequency changes.

## Root Cause

`AccountReplayService.BuildValuation` always stored `PositionReplayStateRecord.DailyPnl` as null. The display metrics then fell back to `(Price - LastClose) * current quantity`. That fallback treated shares bought during the current Beijing natural day as overnight holdings and could create a false profit immediately after a buy.

## Fixed Basis

For each exchange-traded position and Beijing natural day, replay now calculates:

`DailyPnl = EndingMarketValue + TodayPositionNetCashImpact - OpeningPositionValue`

- `EndingMarketValue`: ending quantity multiplied by the current valid real price.
- `OpeningPositionValue`: quantity before the natural-day start multiplied by last close.
- `TodayPositionNetCashImpact`: buy cash outflows, sell cash inflows, dividends, and fees from TradeLog.
- CASH, deposits, and withdrawals are excluded from position PnL.

The result is written to the existing `position_replay_state.daily_pnl` column. Exchange-traded rows no longer reconstruct a missing replay value from current full quantity. Existing SINA_FUND/OTC valuation rules remain unchanged.

## Safety Boundaries

- ETF `quote_time` must be inside the current Beijing natural day.
- Price and last close must both be finite and positive.
- A current-day corporate action returns unavailable rather than guessing.
- Missing or stale quotes return unavailable; `received_at` alone is insufficient.
- Cumulative realized PnL, unrealized PnL, total PnL, return rate, cash, and cost basis retain their existing calculations.

## Regression Coverage

- 159941 same-price current-day buy with zero fee and with fee.
- Overnight holding, add, partial sell, full sell, round trip, and multiple trades.
- Explicit net cash impact, dividends, and exclusion of account funding.
- Missing/stale quote and current-day corporate action.
- Existing replay-state persistence and top/table aggregate consistency.
- OTC natural-day valuation rules and all existing TradeLog atomic-save tests remain covered by the full suite.
