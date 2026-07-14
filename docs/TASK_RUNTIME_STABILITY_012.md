# TASK-RUNTIME-STABILITY-012：长时间运行稳定性与资源健康监测

## 状态与版本

- 目标测试版本：`V8.4.0`
- 正式基线：`v8.3.0` / `233ec97be9b756434ad36916d1d13ddac7ba0351`
- 本任务在人工长时间验收前不提交、不推送、不创建标签。
- 本任务不修改 `docs/LOCKED_MODULES.md`，也不替换桌面正式快捷方式。

## 目标

新增一个独立、轻量、只读的运行健康监测组件，用于观察进程资源、UI Dispatcher 响应、主界面刷新、窗口数量和正常退出状态。监测结果只写入独立的本地健康目录，不写 SQLite，不触发行情、回放、策略、委托或交易。

## 数据目录

```text
%LocalAppData%\CrossETF.Terminal.UiShell.Reference\health\
  runtime-health-yyyyMMdd-pidNNNN.jsonl
  runtime-health-yyyyMMdd-pidNNNN-part2.jsonl
  runtime-health-errors-yyyyMMdd-pidNNNN.log
  reports\
    runtime-health-report-yyyyMMdd-HHmmss.json
    runtime-health-report-yyyyMMdd-HHmmss.txt
```

规则：

- 完整采样每 30 秒一次；Dispatcher 探针每 5 秒一次。
- 每行先在内存中完整序列化，再以 UTF-8 一次追加一个 JSON 对象。
- 文件允许其它进程只读查看；按自然日和 PID 隔离。
- 单文件达到 20 MB 后切换到 `part2`、`part3` 等后续文件。
- 仅在新采样成功写入后清理超过 7 个自然日的受控健康日志。
- 只删除符合健康日志命名规则的文件，不删除 `reports`、数据库、备份、恢复证据或程序日志。
- 文件写入与清理失败只记录去重后的健康错误，不中断主程序。

## 采样字段

采样包括：

- 时间、版本、PID、启动时间、运行时长、健康状态和原因；
- 工作集、私有内存、托管堆、线程、句柄、GC 各代次数、CPU 累计时间；
- 最近和采样周期内最大 Dispatcher 延迟；
- 主窗口状态、可见性和活动状态；
- 最近主刷新开始/完成时间、耗时、成功状态、连续失败和当前运行时长；
- 走势图、手动录入和风险中心窗口数量；
- 采样写入错误、采样耗时和退出请求标记。

禁止采集 TradeLog 内容、交易金额、账户余额、持仓数量、Token、策略参数、行情原始响应、数据库 SQL、用户输入或窗口截图。

## 状态阈值

阈值集中在 `RuntimeHealthThresholds`：

| 指标 | Warning | Critical |
|---|---:|---:|
| Dispatcher 延迟 | 连续 2 次 `>= 2000 ms` | 单次 `>= 8000 ms` |
| 私有内存 | `>= 1.5 GB` | `>= 3 GB` |
| 30 分钟私有内存增长 | `>= 512 MB` | `>= 1 GB` |
| 线程数 | `>= 250` | `>= 500` |
| 句柄数 | `>= 10000` | `>= 20000` |
| 主刷新持续时间 | `> 30 s` | `> 90 s` |

普通 Warning 必须连续两次采样确认；恢复 Normal 必须连续三次正常采样。Critical 阈值立即生效。相同状态不重复产生转换事件。网络请求失败本身不直接触发 Critical，也不会触发自动重启、自动清理或任何业务动作。

内存趋势只使用最近两小时已有样本按时间窗口比较。缺少 30 分钟基准时不作 30 分钟增长判断；允许显示负增长；不调用 `GC.Collect()` 或 `GC.WaitForPendingFinalizers()`。

## 生命周期

- `MainWindow` 构造时创建监测服务，但不启动。
- `MainWindow.Loaded` 后启动一次采样循环和一次 Dispatcher 探针循环。
- 主界面原刷新入口只增加开始/完成通知，完成通知位于 `finally`；原 2 至 4 秒随机刷新规则和调用顺序不变。
- 窗口数量只在 Dispatcher 回调内读取 `Application.Current.Windows`，不保存窗口强引用，也不改变窗口生命周期。
- `MainWindow.Closed` 时先请求停止，最多等待 3 秒，再可重复释放资源。
- 关闭后不再投递新探针，不创建前台线程，也不使用静态事件持有窗口。

## 系统维护界面

现有 `ManualDataEntryWindow` 的“系统维护”页增加“运行稳定性”面板，显示当前状态、运行时长、内存、线程、句柄、Dispatcher、主刷新、窗口数、采样时间、日志目录和状态原因。

按钮：

- `刷新状态`：只读取当前内存快照，不触发行情或数据库操作。
- `打开健康日志目录`：使用 `UseShellExecute` 打开固定健康目录。
- `导出最近24小时报告`：读取本地 JSONL，在 `health\reports` 生成 JSON 和 TXT。

窗口关闭时解除快照事件订阅。该面板不修改 `ManualDataEntryWindow` 标题栏、WindowChrome 或白闪处理。

## 报告

最近 24 小时报告包含版本、时间范围、样本数、状态计数、起始/当前/最低/最高内存、30/60 分钟变化、最大线程和句柄、Dispatcher 最大值和平均值、最大主刷新耗时、状态转换、当前窗口数、运行时长、写入错误数及异常退出证据。

报告不包含 TradeLog、策略参数、账户资金、持仓、Token、行情原文或数据库内容。无样本时仍生成明确的空报告；导出失败不停止监测。

## 自动化测试边界

- 使用临时时钟、临时目录、模拟指标提供器和可控 Dispatcher 适配器。
- 不访问真实行情接口、用户真实数据库、真实 health/backups/restore 目录或桌面快捷方式。
- 不启动长时间真实程序，不强制 GC。
- 保留原 `1047/1047` 基线并覆盖阈值、滞回、并发、停止、文件滚动、保留、报告、UI 和锁定边界。

## 人工验收

测试包独立发布到：

```text
D:\shibaowangETF\artifacts\test\v8.4.0\跨境ETF.exe
```

人工验收至少包括：基础采样、窗口反复打开关闭、两小时运行、报告导出和正常退出。桌面正式快捷方式继续指向 `v8.3.0`，不得以本测试包覆盖正式版本。
