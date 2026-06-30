# TASK-CHART-006: Chart Change Percent Closeout

This note records the display and calculation contract for the `Change` value in `SecurityChartWindow`.

## Data Contract

The chart header change percent is stored as a ratio:

```text
-0.0029 = -0.29%
+0.0119 = +1.19%
```

It is not stored as percentage points.

## Period Rules

Intraday:

```text
change = (latest price - previous close) / previous close
```

The ETF quote `ChangePercent` may be used when available because it is already parsed from the real quote source. If it is missing, the chart falls back to latest price and quote previous close.

Daily:

```text
change = (current daily close - previous daily close) / previous daily close
```

When a real quote updates the current daily K bar for display, the current close is the quote price and the base remains the previous daily close.

Weekly:

```text
change = (current weekly close - previous weekly close) / previous weekly close
```

Monthly:

```text
change = (current monthly close - previous monthly close) / previous monthly close
```

If the previous period close is missing or invalid, the chart displays `--` instead of forcing `0.00%`.

## Display Format

```text
positive: +0.00%
negative: -0.00%
zero    : 0.00%
missing : --
```

Rounded negative zero is normalized to `0.00%`. The formatter does not modify raw quote, intraday, or K-line data.
