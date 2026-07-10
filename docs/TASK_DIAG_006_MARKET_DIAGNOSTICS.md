# TASK-DIAG-006: 风险中心运行诊断模块

## 目标

在现有“风险中心”中嵌入“运行诊断”页签，不新增左侧一级模块，不新增独立顶层诊断窗口。

最终结构：

- 风险中心
  - 风险概览
  - 运行诊断

运行诊断由 `Views/MarketDiagnosticsView.xaml` / `Views/MarketDiagnosticsView.xaml.cs` 承载。

## 页签

运行诊断包含 5 个内部页签：

1. 诊断总览
2. 行情与缓存
3. 今日盈亏审计
4. 运行日志
5. 程序环境

## 只读边界

运行诊断只读取本地状态：

- `market_quote_cache`
- `market_source_status`
- `runtime_log`
- `position_replay_state`
- `otc_position_replay_state`
- 程序程序集与进程信息

“重新读取本地状态”按钮只重新读取 SQLite 和进程/程序集信息，并重新组装诊断快照。

禁止行为：

- 不请求行情接口
- 不执行 live probe
- 不触发市场刷新
- 不触发账户回放
- 不触发策略计算
- 不生成委托草案
- 不写数据库
- 不写 TradeLog
- 不删除缓存
- 不清空日志

## 今日盈亏审计口径

运行诊断不复制第二套今日/当日盈亏规则。

`EtfDecisionTableMetrics` 提供统一逐项评估结果：

- `EvaluateNaturalDayValuationItems(...)`
- `NaturalDayPnlEvaluationItem`

现有 `CalculateNaturalDayValuationDailyPnl(...)` 保持原有业务计算流程。诊断逐项评估复用相同的自然日判断函数，并将诊断合计与业务入口合计做一致性校验。

因此以下三处保持同一判断口径：

- 主界面顶部今日盈亏
- 跨境 ETF 监控表格当日盈亏
- 运行诊断今日盈亏审计

本任务只结构化输出既有判断结果，不改变任何盈亏规则。

同一 `strategy_code + actual_code` 同时存在于持仓回放与场外持仓回放时，诊断层按业务入口的来源优先级合并为一个事件行；不会用 `Distinct` 隐藏重复，也不会改变 `IncludedAmount` 或业务合计。

## 展示口径

- 当前程序集版本为 `8.1.0`，界面短版本显示 `V8.1.0`，程序环境保留完整 informational version。
- 风险中心与运行诊断使用各自局部深色页签样式，不修改全局 `TabItem` 样式。
- 诊断列名使用中文，价格/净值和数量最多显示 4 位小数，盈亏金额显示 2 位小数。
- 整体状态只根据当前数据源、数据库读取、一致性、过期行情与近 30 分钟日志判断；历史日志读取量单独展示，不会让旧错误永久影响当前状态。
- 页面外层不提供横向滚动，横向滚动只保留在各个只读 `DataGrid` 内。
- 风险中心和诊断内部页签内容统一使用 Stretch 布局，普通窗口与最大化状态下均占满可用内容区。
- “关闭”按钮属于 `RiskCenterWindow` 公共底部操作栏，风险概览与运行诊断共用唯一按钮，不放在任一子页内部。

## 禁止修改范围

本任务不得修改：

- `Core/Services/AccountReplayService.cs`
- `Core/Services/StrategyDecisionService.cs`
- `Core/Services/OrderDraftService.cs`
- `Views/ManualDataEntryWindow.xaml`
- `Views/ManualDataEntryWindow.xaml.cs`
- TradeLog 保存和回放逻辑
- 委托草案生成逻辑
- 策略核心买卖规则
- 行情抓取接口、parser、router、scheduler 行为
- 数据库建表结构
- 缓存清理逻辑
- 白闪/标题栏逻辑
- 版本显示逻辑
- 主界面手动刷新按钮

## 验证重点

- 风险中心存在“运行诊断”页签。
- 未新增左侧一级“运行诊断”模块。
- 未新增 `MarketDiagnosticsWindow`。
- 运行诊断 5 个页签全部存在。
- 今日盈亏审计合计与现有今日盈亏入口一致。
- 诊断 service 不调用 save/write/delete/live probe/联网逻辑。
- runtime_log 只读展示 WARN / ERROR。
- 程序环境展示版本、exe 路径、数据库路径。
