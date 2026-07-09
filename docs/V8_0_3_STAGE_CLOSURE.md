# V8.0.3 阶段收口记录

## 稳定基线

- 当前稳定版本：v8.0.3
- Commit：44b3384df8bc8aa3441882e578e9739f318ae04d
- 发布目录：`D:\shibaowangETF\artifacts\release\v8.0.3\`
- 测试基线：869/869

## 已收口内容

- 今日/当日盈亏自然日口径。
- 场内 ETF 旧行情过滤。
- `position.daily_pnl` 自然日过滤。
- 场外 `SINA_FUND` 周末、休市日旧 NAV 重收过滤。
- 顶部今日盈亏与表格当日盈亏同口径。
- 交易日志窗口原生标题栏按钮修复。
- 版本显示从程序集元数据读取。
- 主表指标/策略配置目前收口。
- 策略决策输出目前收口。

## 高风险文件

以下文件和模块已处于高风险收口范围，后续修改前应先做只读审计并明确影响面：

- `Core/Services/EtfDecisionTableMetrics.cs`
- `Tests/Display/EtfDecisionTableMetricsTests.cs`
- `Core/Services/AccountReplayService.cs`
- `Views/ManualDataEntryWindow.xaml`
- `Views/ManualDataEntryWindow.xaml.cs`
- TradeLog 保存相关代码。
- 委托草案相关代码。
- 策略核心买卖规则相关代码。

## 运行注意事项

- 统一运行 v8.0.3 发布目录：`D:\shibaowangETF\artifacts\release\v8.0.3\`
- 不再运行 v8.0.0、v8.0.1、v8.0.2 旧目录做日常验证。
- 如再次出现白闪，先记录复现步骤、窗口名称、触发动作和版本目录，不要与盈亏或策略修复混改。
- 如再次出现盈亏异常，先只读审计组成明细、数据时间和有效项过滤结果，不要盲目修改计算逻辑。

## 后续建议

- 先观察 v8.0.3 一天。
- 若无阻塞，再进入下一模块。
- 下一模块建议从“数据状态可视化 / 日志诊断 / 行情源健康状态”中选择。
- 不建议马上大改盈亏和策略核心。

## 边界确认

- 未修改今日/当日盈亏逻辑。
- 未修改 `EtfDecisionTableMetrics`。
- 未修改 `AccountReplayService.cs`。
- 未修改 TradeLog。
- 未自动写 TradeLog。
- 未修改委托草案。
- 未修改策略核心买卖规则。
- 未修改行情接口。
- 未修改数据库。
- 未清空缓存。
- 未修改白闪/窗口相关逻辑。
- 未新增主界面手动刷新按钮。
- 未提交 artifacts 发布目录。
