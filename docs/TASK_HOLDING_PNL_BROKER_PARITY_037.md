# TASK-HOLDING-PNL-BROKER-PARITY-037

## Scope

- Target version: `8.10.9`.
- Add a broker-compatible display basis for diluted holding cost, holding PnL, and holding return rate.
- Preserve the accounting meanings of `CostAmount`, `AverageCost`, `RealizedPnl`, `UnrealizedPnl`, and `TotalPnl`.
- Preserve the locked natural-day `DailyPnl` formula and refresh-stability behavior.

## Read-Only Reconciliation

The project-external SQLite backup contains eight 159941 TradeLog rows in the current open holding cycle. Their actual net cash impacts produce:

- Current quantity: `4300`.
- Open-cycle net investment: `4470.57`.
- Diluted average cost: `1.03966744186047`.
- Market value at `1.589`: `6832.70`.
- Broker holding PnL: `2362.13`.

The previous display used the replay moving-average book cost `6671.227848599046`, or `1.5514483368834993` per share, so it showed only the remaining unrealized component instead of the broker's open-cycle diluted result.

## Fixed Display Basis

`BrokerHoldingPnlCalculator` derives an independent read-only display model from TradeLog facts:

- `OpenCycleNetInvestment`: buy net outflows minus sell/dividend net inflows in the current open cycle.
- `DilutedCostAmount`: equal to current open-cycle net investment.
- `DilutedAverageCost`: net investment divided by current quantity.
- `BrokerHoldingPnl`: current market value minus current open-cycle net investment.
- `BrokerHoldingReturnRate`: broker holding PnL divided by positive net investment.

A cycle starts when quantity changes from zero to positive and ends when quantity returns to zero. Rebuying after a full close starts a new cycle. Share delivery, split, and merge change quantity without changing investment. A zero or negative net investment does not produce a return-rate division.

## Combined Positions

- Exchange and OTC values combine by amount.
- Multiple OTC channels remain separate open cycles under the same strategy and are summed by amount.
- The mirrored OTC replay collection is used only when the primary replay collection lacks that actual code, preventing duplicate market value.
- Full redemption followed by resubscription starts a new OTC cycle.
- Percentages are calculated from combined amounts; they are never averaged or added.

## Safety Boundaries

- No database schema or persistence changes.
- No TradeLog writes or field changes.
- No changes to account replay accounting fields, cash, principal, total assets, strategy cost inputs, order-draft inputs, natural-day daily PnL, market sources, or K-line logic.
- The broker-compatible values are used only by the main ETF table's displayed holding cost, holding PnL, and holding return rate.

## Regression Coverage

- Exact 159941 open-cycle reconciliation.
- Fee-inclusive buy/sell net cash impacts.
- Full close and rebuy reset.
- Dividend and corporate-action behavior.
- Zero/negative investment return-rate safety.
- Pure exchange, pure OTC, mixed exchange/OTC, and multiple OTC channels.
- OTC mirror de-duplication and full-redemption resubscription.
