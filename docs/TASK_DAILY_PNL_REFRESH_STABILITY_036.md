# TASK-DAILY-PNL-REFRESH-STABILITY-036

## Scope

- Target version: `8.10.8`.
- Preserve the locked daily PnL formula introduced by TASK-DAILY-PNL-COST-BASIS-035.
- Preserve SINA_FUND valuation-event rules, market request routing, request frequency, and database schema.
- TradeLog remains the only transaction fact source.

## Root Cause

The quote cache upsert replaced a valid current-day row unconditionally. A later response with a missing `quote_time`, an older quote date, or missing valuation fields could therefore erase the evidence required by account replay and the UI. In addition, replay was keyed by `received_at`, while replay, table display, and top-level display selected competing quote rows with different ordering rules.

This allowed a valid `daily_pnl` to be recalculated as unavailable during transient refreshes and then reappear when a complete quote arrived.

## Fixed Behavior

- A same-symbol, same-market, same-source cache row cannot be downgraded by a missing or older `quote_time`.
- A complete price/last-close valuation cannot be replaced by an incomplete valuation row.
- Account replay, the main-window quote lookup, and ETF daily-PnL audit use `MarketQuoteFreshnessSelector`.
- Quote selection prioritizes valid and newer `quote_time`, then valuation completeness, before `received_at`.
- The account replay signature contains the Beijing natural day plus `symbol`, `market_type`, `source`, `price`, `last_close`, and `quote_time`.
- A `received_at`-only change does not queue another account replay.
- The Beijing natural day is part of the signature so the previous day's daily PnL is cleared on day rollover.

## Safety Boundaries

- No database schema changes.
- No TradeLog writes or field changes.
- No changes to cash, principal, total-assets, strategy, order-draft, market source, parser, router, scheduler, or K-line logic.
- A failed market request does not delete or blank a previously valid cache row.
- Existing SINA_FUND natural-day behavior is unchanged.

## Regression Coverage

- 159941 current-day buy of 32,000 shares at 1.559 with zero fee remains `0.00` through valid, missing-time, old-date, incomplete, and valid quote refreshes.
- The same sequence with a fee remains equal to the negative fee.
- Multiple quote sources prefer valid valuation time over a later receive-only row.
- `received_at`-only changes keep the replay signature stable.
- `quote_time` and Beijing natural-day changes alter the replay signature.
- A prior-day quote produces an unavailable daily PnL after natural-day rollover.
