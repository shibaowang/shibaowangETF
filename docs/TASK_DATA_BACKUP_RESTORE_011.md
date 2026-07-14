# TASK-DATA-BACKUP-RESTORE-011：本地 SQLite 数据库安全备份与恢复

## 测试版本

- 目标版本：`V8.3.0`
- 正式基线：`v8.2.1` / `d3f66eefd5d7ca8d6fb95e0bdf7658f888671cb7`
- 本轮仅生成独立测试包，不更新正式桌面快捷方式，不创建标签。

## 固定路径

- 正式数据库：`%LocalAppData%\CrossETF.Terminal.UiShell.Reference\cross_etf_terminal.db`
- 备份目录：`%LocalAppData%\CrossETF.Terminal.UiShell.Reference\backups`
- 恢复暂存目录：`%LocalAppData%\CrossETF.Terminal.UiShell.Reference\restore`

程序名称变为 `跨境ETF.exe` 不改变 `AppFolderName`、`DatabaseFileName`、程序集名称、连接字符串或数据库表结构。

## 一致性备份

活动数据库备份使用 `Microsoft.Data.Sqlite.SqliteConnection.BackupDatabase`，不直接复制活动主数据库文件。流程为：

1. 打开只读源连接和临时目标连接。
2. 在线复制一致性快照到 `<最终备份名>.tmp`。
3. 关闭连接后，以只读模式执行 `PRAGMA integrity_check;`。
4. 结果必须严格等于 `ok`，并确认 `strategy_config`、`trade_log`、`app_settings` 三个基础表存在。
5. 校验通过后，同目录原子移动为最终 `.db`；失败时删除本次临时文件，不留下看似有效的备份。

备份过程不强制 checkpoint，不删除活动库 WAL/SHM，不修改 journal mode，不修改任何用户表。

## 备份种类与保留

受控文件名为：

`cross_etf_terminal_yyyyMMdd_HHmmssfff_V8.3.0_<类型>.db`

类型仅允许：`daily`、`manual`、`preupgrade`、`prerestore`。

- 程序升级前，在 `App.OnStartup`、`base.OnStartup(e)`、`MainWindow`、`LocalDataRepository` 和 `LocalDatabase.Initialize` 之前检查版本并创建 `preupgrade`。
- 数据库不存在时视为首次安装，不创建空备份。
- 版本键缺失或与当前程序集版本不同均先备份；失败会阻止数据库初始化和主窗口创建。
- 初始化成功后写入 `database.last_successful_version`，并在首次行情刷新前检查当天 `daily`。
- 当天有效 `daily` 或 `preupgrade` 已存在时不重复创建；手动备份不受每日一次限制。
- 新备份校验成功后最多保留最近 30 份有效受控备份。非受控文件、待恢复请求、恢复前安全备份引用和本次新备份不会被误删；旧文件删除失败只记录警告，不使新备份失败。

同进程使用 `SemaphoreSlim` 串行，跨进程使用 `backups\.backup.lock` 与 `FileShare.None` 独占锁。服务不增加数据库长连接，保持现有 `Pooling=false`。

## 安全恢复

运行中的程序不直接替换正式数据库。系统维护页只允许选择受控备份目录中的有效备份，并要求两次确认：

1. 显示备份文件名、时间、版本、替换提示、未保存编辑提示和重新启动提示。
2. 明确确认“确认恢复此备份并关闭程序”。

确认后先将备份复制并校验为 `restore\pending_restore.db`，计算 SHA-256，最后原子写入 `pending_restore.json`。marker 不包含任意目标路径，正式恢复目标始终为固定数据库路径。程序正常关闭，用户下次启动时才执行恢复。

启动顺序为：注册异常处理、处理 pending restore、升级前备份判断、`base.OnStartup(e)`、创建主窗口。恢复前若当前库存在，必须先通过在线备份创建并校验 `prerestore`。替换前将旧 WAL/SHM 移到受控证据位置，使用同卷原子替换，替换后再次只读校验。

- 恢复成功：删除 pending 和临时候选文件，保留 `prerestore`。
- 替换后校验失败：用 `prerestore` 回滚并再次校验。
- 回滚成功：继续使用原数据启动，并提示“恢复失败，原数据库已安全恢复”。
- 回滚失败：保留恢复证据，阻止 `MainWindow` 和数据库初始化，严禁伪装成功。

恢复结果写入 `restore_result.json`，下次正常界面启动时提示一次后删除。恢复本身不是交易，不写 order finalization，不自动写 TradeLog，不修改 TradeLog ID 或事实字段。

## 系统维护界面

现有 `ManualDataEntryWindow` 的“系统维护”页新增“数据库备份与恢复”面板，不新建主窗口，也不修改原生标题栏、DWM 或白闪处理。面板显示当前数据库路径、备份目录、最近有效备份、有效数量、自动备份状态和操作状态，并提供：

- 立即备份
- 刷新列表
- 打开备份目录
- 恢复选中备份

无效备份和未选中状态不能恢复。界面不提供任意文件选择器，不允许编辑路径，不显示 Token、TradeLog 内容或数据库内部敏感字段。手动备份只包含已保存数据。

## 业务边界

本任务不修改：

- TradeLog 保存、删除和回放口径
- `AccountReplayService`、持仓回放
- 策略决策、委托草案、委托定稿
- 行情接口、parser、router、scheduler
- 图表、K 线、风险中心诊断
- 数据库结构和正式路径
- `ManualDataEntryWindow` 标题栏与白闪逻辑

本功能仅在当前 Windows 用户的本地目录保存备份，不提供云备份、网络上传、FTP、网盘同步、管理员服务或额外 helper 进程。

## 自动化测试隔离

所有备份/恢复自动化测试只创建 `%TEMP%` 下的独立目录和临时 SQLite 数据库，不读取、不写入、不删除正式数据库或用户真实 `backups` 目录。测试覆盖 WAL 已提交数据、完整性与基础表、受控命名、每日去重、30 份保留、并发锁、恢复暂存、SHA-256、启动恢复、回滚、数据真实性、UI 和版本边界。

## 人工验收

测试包路径：`D:\shibaowangETF\artifacts\test\v8.3.0\跨境ETF.exe`。

人工验收前保留 `v8.2.1` 正式目录及桌面快捷方式。依次验证升级前保护、同日每日去重、手动备份、双确认恢复、取消恢复和恢复前安全备份；损坏恢复与 30 份保留仅使用临时自动化环境验证，不故意破坏用户正式数据库。
