using CrossETF.Terminal.UiShell.Reference.Core.Models;

namespace CrossETF.Terminal.UiShell.Reference.Core.Mocks;

/// <summary>
/// V8.2 场景 Mock TradeLog 工厂，覆盖全部核心交易场景。
/// 所有数据为纯内存假数据，不涉及真实文件读写。
/// </summary>
public static class V8MockTradeLogFactory
{
    public static List<TradeLogEntry> CreateDepositOnly(double amount = 1_000_000, double fee = 0)
    {
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = amount, Fee = fee,
                NetCashImpact = amount - fee,
                CashBalance = amount - fee, TotalAssets = amount - fee,
            }
        };
    }

    public static List<TradeLogEntry> CreateDepositWithFee(double amount = 1_000_000, double fee = 5)
    {
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = amount, Fee = fee,
                NetCashImpact = amount - fee,
                CashBalance = amount - fee, TotalAssets = amount - fee,
            }
        };
    }

    public static List<TradeLogEntry> CreateWithdrawal(double amount = 100_000, double fee = 5)
    {
        double initialCash = 1_000_000;
        double netImpact = -(amount + fee);
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = 1_000_000, Fee = 0,
                NetCashImpact = 1_000_000,
                CashBalance = 1_000_000, TotalAssets = 1_000_000,
            },
            new()
            {
                RowIndex = 2, Time = new DateTime(2025, 2, 1, 10, 0, 0),
                StrategyCode = "CASH", Action = "出金",
                Amount = amount, Fee = fee,
                NetCashImpact = netImpact,
                CashBalance = initialCash + netImpact, TotalAssets = initialCash + netImpact,
            }
        };
    }

    public static List<TradeLogEntry> CreateStrategicBaseBuy(string symbol = "159509", double qty = 15000, double price = 1.284)
    {
        double amt = qty * price;
        double cash = 1_000_000 - amt;
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = 1_000_000, Fee = 0,
                NetCashImpact = 1_000_000,
                CashBalance = 1_000_000, TotalAssets = 1_000_000,
            },
            new()
            {
                RowIndex = 2, Time = new DateTime(2025, 1, 3, 10, 0, 0),
                StrategyCode = symbol, ActualCode = symbol,
                Action = "买入", Price = price, Quantity = qty, Amount = amt,
                Tier = "战略底仓", Source = "场内直投",
                Fee = 0, NetCashImpact = -amt,
                CashBalance = cash, TotalAssets = cash + amt,
                Principal = 1_000_000
            }
        };
    }

    public static List<TradeLogEntry> CreateEtfBuy(string symbol = "159509", double qty = 1000, double price = 1.284, string tier = "狙击一档")
    {
        double amt = qty * price;
        double deposit = 1_000_000;
        double cash = deposit - amt;
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = deposit, Fee = 0,
                NetCashImpact = deposit,
                CashBalance = deposit, TotalAssets = deposit,
            },
            new()
            {
                RowIndex = 2, Time = new DateTime(2025, 1, 5, 14, 0, 0),
                StrategyCode = symbol, ActualCode = symbol,
                Action = "买入", Price = price, Quantity = qty, Amount = amt,
                Tier = tier, Source = "场内直投",
                Fee = 0, NetCashImpact = -amt,
                CashBalance = cash, TotalAssets = cash + amt,
                Principal = deposit
            }
        };
    }

    public static List<TradeLogEntry> CreateOtcBuy(string symbol = "159509", double amt = 50000, double price = 1.284, string tier = "狙击二档")
    {
        double shares = amt / price;
        double deposit = 1_000_000;
        double cash = deposit - amt;
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = deposit, Fee = 0,
                NetCashImpact = deposit,
                CashBalance = deposit, TotalAssets = deposit,
            },
            new()
            {
                RowIndex = 2, Time = new DateTime(2025, 1, 6, 11, 0, 0),
                StrategyCode = symbol, ActualCode = "017091",
                Action = "买入", Price = price, Quantity = shares, Amount = amt,
                Tier = tier, Source = "场外替代",
                Fee = 0, NetCashImpact = -amt,
                CashBalance = cash, TotalAssets = cash + amt,
                Principal = deposit
            }
        };
    }

    public static List<TradeLogEntry> CreateTier1Buy(string symbol = "159509", double qty = 3000, double price = 1.20)
    {
        return CreateEtfBuy(symbol, qty, price, "狙击一档");
    }

    public static List<TradeLogEntry> CreateTier2Buy(string symbol = "159509", double qty = 6200, double price = 1.15)
    {
        return CreateEtfBuy(symbol, qty, price, "狙击二档");
    }

    public static List<TradeLogEntry> CreateSellWithRealizedPnl(string symbol = "159509", double buyQty = 5000, double buyPrice = 1.20, double sellQty = 2000, double sellPrice = 1.50)
    {
        double buyAmt = buyQty * buyPrice;
        double sellAmt = sellQty * sellPrice;
        double deposit = 1_000_000;
        double cashAfterBuy = deposit - buyAmt;
        double fee = sellAmt * 0.00013;
        double costPart = (buyAmt / buyQty) * sellQty;
        double realizedPnl = sellAmt - fee - costPart;
        double cashAfterSell = cashAfterBuy + (sellAmt - fee);
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = deposit, Fee = 0,
                NetCashImpact = deposit,
                CashBalance = deposit, TotalAssets = deposit,
            },
            new()
            {
                RowIndex = 2, Time = new DateTime(2025, 1, 5, 10, 0, 0),
                StrategyCode = symbol, ActualCode = symbol,
                Action = "买入", Price = buyPrice, Quantity = buyQty, Amount = buyAmt,
                Tier = "战略底仓", Source = "场内直投",
                Fee = 0, NetCashImpact = -buyAmt,
                CashBalance = cashAfterBuy, TotalAssets = cashAfterBuy + buyAmt,
                Principal = deposit
            },
            new()
            {
                RowIndex = 3, Time = new DateTime(2025, 3, 1, 14, 0, 0),
                StrategyCode = symbol, ActualCode = symbol,
                Action = "卖出", Price = sellPrice, Quantity = sellQty, Amount = sellAmt,
                Tier = "", Source = "场内直投",
                Fee = fee, NetCashImpact = sellAmt - fee,
                CashBalance = cashAfterSell, TotalAssets = cashAfterSell + (buyQty - sellQty) * sellPrice,
                Principal = deposit + realizedPnl
            }
        };
    }

    public static List<TradeLogEntry> CreateTradeWithFee(string symbol = "159509")
    {
        double deposit = 1_000_000;
        double fee = 5;
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = deposit, Fee = fee,
                NetCashImpact = deposit - fee,
                CashBalance = deposit - fee, TotalAssets = deposit - fee,
            }
        };
    }

    public static List<TradeLogEntry> CreateDividend(string symbol = "159509", double divAmt = 5000, double fee = 0)
    {
        double deposit = 1_000_000;
        double cash = deposit + divAmt - fee;
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = deposit, Fee = 0,
                NetCashImpact = deposit,
                CashBalance = deposit, TotalAssets = deposit,
            },
            new()
            {
                RowIndex = 2, Time = new DateTime(2025, 6, 15, 9, 0, 0),
                StrategyCode = symbol, ActualCode = symbol,
                Action = "分红", Amount = divAmt, Fee = fee,
                NetCashImpact = divAmt - fee,
                CashBalance = cash, TotalAssets = cash,
            }
        };
    }

    public static List<TradeLogEntry> CreateBonusShares(string symbol = "159509", double bonusQty = 500)
    {
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = 1_000_000, Fee = 0,
                NetCashImpact = 1_000_000,
                CashBalance = 1_000_000, TotalAssets = 1_000_000,
            },
            new()
            {
                RowIndex = 2, Time = new DateTime(2025, 4, 1, 9, 0, 0),
                StrategyCode = symbol, ActualCode = symbol,
                Action = "送股", Quantity = bonusQty, Amount = 0,
                Tier = "", Source = "场内直投",
                Fee = 0, NetCashImpact = 0,
                CashBalance = 1_000_000, TotalAssets = 1_000_000,
            }
        };
    }

    public static List<TradeLogEntry> CreateSplit(string symbol = "159509", double splitFactor = 2.0, double existingQty = 5000)
    {
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = 1_000_000, Fee = 0,
                NetCashImpact = 1_000_000,
                CashBalance = 1_000_000, TotalAssets = 1_000_000,
            },
            new()
            {
                RowIndex = 2, Time = new DateTime(2025, 5, 1, 9, 0, 0),
                StrategyCode = symbol, ActualCode = symbol,
                Action = "拆分", Quantity = existingQty * (splitFactor - 1), Amount = 0,
                Tier = "", Source = "场内直投",
                Fee = 0, NetCashImpact = 0,
                CashBalance = 1_000_000, TotalAssets = 1_000_000,
            }
        };
    }

    public static List<TradeLogEntry> CreateMerge(string symbol = "159509", double reduceQty = 2500)
    {
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = 1_000_000, Fee = 0,
                NetCashImpact = 1_000_000,
                CashBalance = 1_000_000, TotalAssets = 1_000_000,
            },
            new()
            {
                RowIndex = 2, Time = new DateTime(2025, 7, 1, 9, 0, 0),
                StrategyCode = symbol, ActualCode = symbol,
                Action = "合并", Quantity = reduceQty, Amount = 0,
                Tier = "", Source = "场内直投",
                Fee = 0, NetCashImpact = 0,
                CashBalance = 1_000_000, TotalAssets = 1_000_000,
            }
        };
    }

    public static List<TradeLogEntry> CreateAdjustment(string symbol = "159509", double factor = 1.05)
    {
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = 1_000_000, Fee = 0,
                NetCashImpact = 1_000_000,
                CashBalance = 1_000_000, TotalAssets = 1_000_000,
            },
            new()
            {
                RowIndex = 2, Time = new DateTime(2025, 8, 1, 9, 0, 0),
                StrategyCode = symbol, ActualCode = symbol,
                Action = "除权校准", Quantity = factor, Amount = 0,
                Tier = "", Source = "场内直投",
                Fee = 0, NetCashImpact = 0,
                CashBalance = 1_000_000, TotalAssets = 1_000_000,
            }
        };
    }

    public static List<TradeLogEntry> CreateCycleEnd(double cashBalance = 800_000)
    {
        double deposit = 1_000_000;
        double buyAmt = deposit - cashBalance;
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = deposit, Fee = 0,
                NetCashImpact = deposit,
                CashBalance = deposit, TotalAssets = deposit,
            },
            new()
            {
                RowIndex = 2, Time = new DateTime(2025, 3, 15, 10, 0, 0),
                StrategyCode = "159509", ActualCode = "159509",
                Action = "买入", Price = 1.20, Quantity = buyAmt / 1.20, Amount = buyAmt,
                Tier = "战略底仓", Source = "场内直投",
                Fee = 0, NetCashImpact = -buyAmt,
                CashBalance = cashBalance, TotalAssets = cashBalance + buyAmt,
            },
            new()
            {
                RowIndex = 3, Time = new DateTime(2025, 6, 30, 15, 0, 0),
                StrategyCode = "CASH", Action = "CASH",
                Tier = "周期结束",
                CashBalance = cashBalance, TotalAssets = cashBalance + buyAmt,
            }
        };
    }

    public static List<TradeLogEntry> CreateOtcMultiChannelBuy(string symbol = "159509", double needAmt = 100000)
    {
        double deposit = 500_000;
        double cash = deposit - needAmt;
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = deposit, Fee = 0,
                NetCashImpact = deposit,
                CashBalance = deposit, TotalAssets = deposit,
            },
            new()
            {
                RowIndex = 2, Time = new DateTime(2025, 1, 10, 10, 0, 0),
                StrategyCode = symbol, ActualCode = "017091",
                Action = "买入", Price = 1.284, Quantity = 40000 / 1.284, Amount = 40000,
                Tier = "狙击一档", Source = "场外替代",
                Fee = 0, NetCashImpact = -40000,
                CashBalance = deposit - 40000, TotalAssets = deposit,
            },
            new()
            {
                RowIndex = 3, Time = new DateTime(2025, 1, 10, 10, 1, 0),
                StrategyCode = symbol, ActualCode = "017092",
                Action = "买入", Price = 1.285, Quantity = 60000 / 1.285, Amount = cash - (deposit - needAmt) > 0 ? needAmt - 40000 : cash - (deposit - needAmt),
                Tier = "狙击一档", Source = "场外替代",
                Fee = 0, NetCashImpact = -(needAmt - 40000),
                CashBalance = cash, TotalAssets = cash + needAmt,
            }
        };
    }

    public static List<TradeLogEntry> CreateOtcCClassSell(string symbol = "159509", double sellAmt = 30000, double sellPrice = 1.35)
    {
        double deposit = 1_000_000;
        double buyAmt = 100000;
        double cashAfterBuy = deposit - buyAmt;
        double fee = sellAmt * 0.00013;
        return new List<TradeLogEntry>
        {
            new()
            {
                RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
                StrategyCode = "CASH", Action = "入金",
                Amount = deposit, Fee = 0,
                NetCashImpact = deposit,
                CashBalance = deposit, TotalAssets = deposit,
            },
            new()
            {
                RowIndex = 2, Time = new DateTime(2025, 1, 10, 10, 0, 0),
                StrategyCode = symbol, ActualCode = "017091",
                Action = "买入", Price = 1.20, Quantity = buyAmt / 1.20, Amount = buyAmt,
                Tier = "战略底仓", Source = "场外替代",
                Fee = 0, NetCashImpact = -buyAmt,
                CashBalance = cashAfterBuy, TotalAssets = cashAfterBuy + buyAmt,
            },
            new()
            {
                RowIndex = 3, Time = new DateTime(2025, 3, 20, 14, 0, 0),
                StrategyCode = symbol, ActualCode = "017092",
                Action = "卖出", Price = sellPrice, Quantity = sellAmt / sellPrice, Amount = sellAmt,
                Tier = "", Source = "场外替代",
                Fee = fee, NetCashImpact = sellAmt - fee,
                CashBalance = cashAfterBuy + sellAmt - fee, TotalAssets = cashAfterBuy + sellAmt - fee + (buyAmt - sellAmt),
            }
        };
    }

    /// <summary>
    /// 完整场景：入金 -> 战略底仓 -> 狙击一档 -> 狙击二档 -> 卖出获利 -> 分红。
    /// </summary>
    public static List<TradeLogEntry> CreateFullScenario()
    {
        double deposit = 1_000_000;
        double baseBuyAmt = 200_000;    // 20% 底仓
        double tier1Amt = 30000;
        double tier2Amt = 60000;
        double sellAmt = 50000;
        double fee = sellAmt * 0.00013;
        double divAmt = 8000;

        double cash = deposit;
        var entries = new List<TradeLogEntry>();

        // 1. 入金
        entries.Add(new TradeLogEntry
        {
            RowIndex = 1, Time = new DateTime(2025, 1, 2, 9, 30, 0),
            StrategyCode = "CASH", Action = "入金",
            Amount = deposit, Fee = 0, NetCashImpact = deposit,
            CashBalance = deposit, TotalAssets = deposit
        });

        // 2. 战略底仓买入
        cash -= baseBuyAmt;
        entries.Add(new TradeLogEntry
        {
            RowIndex = 2, Time = new DateTime(2025, 1, 3, 10, 0, 0),
            StrategyCode = "159509", ActualCode = "159509",
            Action = "买入", Price = 1.284, Quantity = baseBuyAmt / 1.284, Amount = baseBuyAmt,
            Tier = "战略底仓", Source = "场内直投",
            Fee = 0, NetCashImpact = -baseBuyAmt,
            CashBalance = cash, TotalAssets = cash + baseBuyAmt
        });

        // 3. 狙击一档买入
        cash -= tier1Amt;
        entries.Add(new TradeLogEntry
        {
            RowIndex = 3, Time = new DateTime(2025, 2, 15, 14, 0, 0),
            StrategyCode = "159509", ActualCode = "159509",
            Action = "买入", Price = 1.20, Quantity = tier1Amt / 1.20, Amount = tier1Amt,
            Tier = "狙击一档", Source = "场内直投",
            Fee = 0, NetCashImpact = -tier1Amt,
            CashBalance = cash, TotalAssets = cash + baseBuyAmt + tier1Amt
        });

        // 4. 狙击二档买入
        cash -= tier2Amt;
        entries.Add(new TradeLogEntry
        {
            RowIndex = 4, Time = new DateTime(2025, 3, 1, 11, 0, 0),
            StrategyCode = "159509", ActualCode = "159509",
            Action = "买入", Price = 1.10, Quantity = tier2Amt / 1.10, Amount = tier2Amt,
            Tier = "狙击二档", Source = "场内直投",
            Fee = 0, NetCashImpact = -tier2Amt,
            CashBalance = cash, TotalAssets = cash + baseBuyAmt + tier1Amt + tier2Amt
        });

        // 5. 卖出获利
        cash += (sellAmt - fee);
        entries.Add(new TradeLogEntry
        {
            RowIndex = 5, Time = new DateTime(2025, 4, 15, 14, 30, 0),
            StrategyCode = "159509", ActualCode = "159509",
            Action = "卖出", Price = 1.50, Quantity = sellAmt / 1.50, Amount = sellAmt,
            Tier = "", Source = "场内直投",
            Fee = fee, NetCashImpact = sellAmt - fee,
            CashBalance = cash, TotalAssets = cash + baseBuyAmt + tier1Amt + tier2Amt - sellAmt
        });

        // 6. 分红
        cash += divAmt;
        entries.Add(new TradeLogEntry
        {
            RowIndex = 6, Time = new DateTime(2025, 6, 15, 9, 0, 0),
            StrategyCode = "159509", ActualCode = "159509",
            Action = "分红", Amount = divAmt, Fee = 0, NetCashImpact = divAmt,
            CashBalance = cash, TotalAssets = cash + baseBuyAmt + tier1Amt + tier2Amt - sellAmt
        });

        return entries;
    }

    /// <summary>
    /// 创建带表头的 TradeLog 模拟数据，用于 Schema 校验测试。
    /// </summary>
    public static (List<string> Headers, List<TradeLogEntry> Entries) CreateWithHeaders()
    {
        var headers = TradeLogEntry.RequiredHeaders.ToList();
        var entries = CreateFullScenario();
        return (headers, entries);
    }
}
