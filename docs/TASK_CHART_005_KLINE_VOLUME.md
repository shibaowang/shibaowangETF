# TASK-CHART-005: K-line Volume Field Closeout

This note records the final display contract for daily, weekly, and monthly K-line volume in the ETF security chart window.

## EastMoney K-line Fields

EastMoney historical K-line rows requested with `fields2=f51..f61` are parsed as:

```text
f51 = date
f52 = open
f53 = close
f54 = high
f55 = low
f56 = volume
f57 = amount
f58 = amplitude
f59 = change percent
f60 = change amount
f61 = turnover
```

The chart window uses only `f56` for K-line volume and only `f57` for amount. It must not use open, close, high, low, or realtime quote fields as volume.

## Weekly And Monthly Aggregation

Weekly and monthly K-lines are aggregated from real daily OHLCV:

```text
open   = first trading day open
high   = max high in the period
low    = min low in the period
close  = last trading day close
volume = sum of daily volume
amount = sum of daily amount
date   = last trading day
```

A realtime quote may update the current K bar close, high, and low for display. It does not overwrite or fabricate K-line volume.

## Volume Bar Color

K-line volume color is based on that K-line's open and close:

```text
Close >= Open: red volume bar
Close < Open : green volume bar
```

This is separate from intraday volume color, which compares adjacent minute prices.

## Missing Volume

Missing or zero K-line volume draws no fake bar. If all K-line volumes are unavailable, the volume panel shows the unavailable state.
