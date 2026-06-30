# Phase 2A-Core-Foundation：V8.2 Core 业务地基完成文档

## 1. 本阶段目标

在不修改 UI 壳的前提下，一次性迁移 V8.2 VBA 核心业务规则到 .NET，建立纯内存的账户事实源和交易决策引擎。

## 2. V8.2 VBA 对齐清单

| V8.2 规则 | .NET 实现 | 状态 |
|-----------|-----------|------|
| TradeLog 15 列 Schema | `TradeLogEntry` / `TradeLogSchemaValidator` | 完成 |
| PreCheck 入金/出金/买卖 | `TradeLogPreCheckService` | 完成 |
| Replay 重演现金/持仓/成本/盈亏 | `TradeLogReplayService` | 完成 |
| PrincipalBase 不含浮盈浮亏 | `PrincipalBaseCalculator` | 完成 |
| 20% 底仓可配置 | `BasePositionService` + `StrategySettings.BasePositionRatio` | 完成 |
| 狙击资金池 = PrincipalBase - 战略底仓 | `RealSniperPoolBudgetService` | 完成 |
| T1-T6 权重 1/2/4/8/16/32 总 63 | `TierEngine` | 完成 |
| 累计档位目标 + 深回撤优先 | `TierEngine` | 完成 |
| GetTierExecutedAmt 只统计买入 | `TierEngine` | 完成 |
| 场内向上取整 100 股 / 场外到分 | `TradeQuantifier` | 完成 |
| 底仓保护按剩余持仓成本 | `BasePositionService` + `PositionProtectionService` | 完成 |
| OTCMap 8 列 + 多通道拆单 | `OtcChannel` + `OtcMapService` | 完成 |
| C 类优先卖出 + 按实际代码扣减成本 | `OtcMapService` | 完成 |

## 3. 已实现服务清单

### Models (8 个)
- `TradeLogEntry` — 15 列模型，valid actions/tiers 常量
- `HoldingSnapshot` — 持仓快照（场内/场外分离）
- `StrategySettings` — 策略配置（BasePositionRatio 可配置）
- `OtcChannel` — OTCMap 8 列模型
- `TradeLogReplayResult` — Replay 输出
- `QuantifiedTradeResult` — 量化交易结果
- `TierExecutionSummary` — 档位执行摘要
- `OtcTradeLeg` — 场外多通道交易腿

### Services (10 个)
- `TradeLogSchemaValidator` — 15 列表头校验
- `TradeLogPreCheckService` — 入金/出金/买卖预检
- `TradeLogReplayService` — 现金/持仓/成本/盈亏重演
- `PrincipalBaseCalculator` — 本金基准计算
- `BasePositionService` — 底仓目标/完成度/保护判断
- `RealSniperPoolBudgetService` — 狙击资金池计算
- `TierEngine` — T1-T6 回撤狙击引擎
- `TradeQuantifier` — 场内场外交易量化
- `PositionProtectionService` — 卖出底仓保护
- `OtcMapService` — OTCMap A/C 多通道管理

### Mock
- `V8MockTradeLogFactory` — 17 个场景的 TradeLog 假数据工厂

## 4. 未实现且本阶段刻意不做的内容

- 真实行情（东方财富/腾讯/新浪）
- UI 绑定 / ViewModel / Projection
- 自动刷新 / Timer / BackgroundService
- 真实 TradeLog 文件读取/写入
- 券商/银行/交易接口
- 申购/赎回/资金划转
- 回撤图真实数据源
- 顶部指数栏真实数据

## 5. 测试覆盖清单

| 测试组 | 用例数 | 覆盖内容 |
|--------|--------|----------|
| TradeLogSchemaTests | 6 | 15列通过/缺列/重复/扩展列/空表头 |
| TradeLogPreCheckTests | 11 | 入金/出金/负金额/非法动作/非数值/手续费 |
| TradeLogReplayTests | 11 | 买入/卖出/分红/送股/拆分/合并/除权/Otc/超卖/现金一致/PrincipalBase |
| PrincipalBaseTests | 5 | 不含浮盈/手续费/已实现盈亏/分红/totalAssets*0.8禁止 |
| BasePositionTests | 5 | 默认20%/可改50%/BaseNeed/卖出安全/成本非市值 |
| RealSniperPoolBudgetTests | 5 | 第一轮/非0.8/周期结束/价格不变/63份拆分 |
| TierEngineTests | 9 | 权重63/六档触发/深回撤优先/累计目标/买入only/卖出不重置/分红不计/无85%/执行摘要 |
| TradeQuantifierTests | 9 | 向上取整100股/向下取整/到分/到0.0001/现金不足/净现金流正负/手续费/非买卖 |
| OtcMapTests | 9 | 8列模型/Disabled/优先级/每日限额/最小申购/C类优先/真实持仓/实际成本 |
| RegressionBoundaryTests | 6 | Core不依赖MainWindow/WPF/HttpClient/不写文件/Mock不读盘 |
| **合计** | **84** | |

## 6. 安全边界

- 零网络请求
- 零 TradeLog 文件读写
- 零券商/银行/交易连接
- MainWindow.xaml / MainWindow.xaml.cs / Themes/DarkTheme.xaml 未修改
- docs/UI_LAYOUT_LOCK.md 未修改
- Core 层不依赖 WPF 控件
- 所有数据为纯内存假数据

## 7. 下一阶段建议

**Phase 2B：Projection / ViewModel 只读绑定层**

建议内容：
- 创建 ViewModel 层，将 Core 服务输出映射为 WPF 可绑定属性
- 替换 ETF 表格、回撤图、TradeLog、资金池的静态假数据为 ViewModel 绑定
- 仍不接真实行情，仍不读取真实 TradeLog 文件
- 仍不接自动刷新、不接交易接口

关键约束：
- 禁止修改 MainWindow.xaml 布局结构
- 禁止修改 ETF 表头拖拽排序/点击排序
- 只替换数据源，不改控件树
