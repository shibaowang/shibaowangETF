# TASK-DAILY-KLINE-PERSISTENCE-034

## Scope

- Target version: `8.10.6`.
- ETF formal daily history remains `TENCENT_DAILY_QFQ`.
- Index formal daily history remains `EASTMONEY_HISTORY`.
- Quote-generated `QUOTE_INTRADAY_BAR` points remain display-only and are never persisted.

## Root Cause

The daily chart refresh path previously treated any non-empty DailyLike cache as sufficient. It did not compare the latest real daily point with the latest completed trading date. The quote layer could therefore display a temporary bar for 2026-07-20 while the persisted DailyLike history still ended on 2026-07-10. When the quote date changed to 2026-07-21, the temporary 2026-07-20 bar was replaced and disappeared.

The regular EastMoney index daily refresh also did not call the existing history persistence entry. Tencent ETF persistence stored the returned rolling payload without first merging it with deeper retained history.

## Fixed Behavior

- Freshness uses only valid formal daily points. `IsDisplayOnly` points and every `QUOTE_*` point source are excluded.
- The expected completed date uses Beijing/A-share close for ETFs and US Eastern close for indices.
- Stale tails trigger a scheduler-gated provider request. The request target key includes provider, strategy code, and expected completed trading date.
- Background catch-up processes at most one symbol per refresh round.
- Incoming formal daily points merge with existing formal history by `Date.Date`; incoming points replace the same date while older deep history is retained.
- The merged formal payload is marked `MERGED_REAL_DAILY_LIKE` and is written through the existing `market_history_cache` persistence entry.
- Missing weekdays, weekends, holidays, prices, and volumes are never synthesized.
- Weekly and monthly views continue to aggregate the merged DailyLike history and do not use separate network routes.
- A failed refresh leaves the previous formal cache intact and continues through the existing scheduler and circuit-breaker behavior.

## Automated Coverage

- Reproduces the temporary 2026-07-20 quote bar disappearing when the quote advances to 2026-07-21 without formal persistence.
- Verifies formal recovery of 2026-07-13 through 2026-07-20 without 2026-07-11/12 or a fabricated 2026-07-21 formal point.
- Verifies restart reconstruction from the persisted merged payload.
- Verifies 3000-point deep-history retention, same-date overwrite, ETF/Index routing, failure preservation, scheduler use, and the one-symbol background limit.
