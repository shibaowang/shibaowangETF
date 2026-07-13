# TASK-RELEASE-PACKAGING-010：发布文件中文命名与桌面快捷方式

## 正式发布规范

1. 从 `V8.2.1` 开始，正式发布目录中的用户启动文件统一命名为 `跨境ETF.exe`。
2. 不修改程序集 `AssemblyName`；程序集身份、DLL、deps、runtimeconfig、PDB 及其它依赖继续使用原名称。
3. 统一使用 `scripts/Publish-CrossEtfRelease.ps1` 在 `dotnet publish` 完成后重命名 AppHost EXE。
4. 正式发布目录不得保留 `CrossETF.Terminal.UiShell.Reference.exe`。
5. 当前用户桌面快捷方式统一命名为 `跨境ETF.lnk`，并覆盖更新同一个快捷方式。
6. 快捷方式始终指向当前最新正式版本的 `跨境ETF.exe`，工作目录为对应正式发布目录，图标来自正式 EXE。
7. 发布前必须验证 detached worktree 的 HEAD 与 `ExpectedCommit` 完整哈希一致。
8. 发布后必须验证 `FileVersion`，且 `ProductVersion/InformationalVersion` 必须包含最终锁定提交哈希。
9. 正式 EXE 必须真实运行至少 8 秒并通过正常关闭验证；快捷方式安装后也必须验证能够启动同一 EXE。
10. 只有 EXE 验证成功后才可创建临时快捷方式；临时快捷方式属性验证成功后才替换正式快捷方式。
11. `artifacts` 和桌面 `.lnk` 均不得提交到 Git。
12. `v8.2.0` 及更早发布目录不追溯改名。
13. 后续正式版本必须继续使用同一脚本发布，不得手工改变程序集身份来实现中文文件名。

## 脚本参数

- `Version`：三段式版本号，例如 `8.2.1`。
- `SourcePath`（别名 `WorktreePath`）：最终标签 detached worktree 路径。
- `OutputRoot`：发布根目录，默认 `D:\shibaowangETF\artifacts\release`。
- `CreateDesktopShortcut`：验证发布 EXE 后创建或更新当前用户桌面快捷方式。
- `ExpectedCommit`：最终锁定提交的完整 40 位哈希。

脚本失败时返回非零退出码，不使用强制终止进程，不用无效快捷方式覆盖原有可用快捷方式。
