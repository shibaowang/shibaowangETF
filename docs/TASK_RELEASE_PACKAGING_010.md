# TASK-RELEASE-PACKAGING-010：发布文件中文命名与桌面快捷方式

## 正式发布规范

1. 从 `V8.2.1` 开始，正式发布目录中的用户启动文件统一命名为 `跨境ETF.exe`。
2. 不修改程序集 `AssemblyName`；程序集身份、DLL、deps、runtimeconfig 及其它运行依赖继续使用原名称。从 `V8.10.3` 开始正式包禁止 PDB。
3. 统一使用 `scripts/Publish-CrossEtfRelease.ps1` 在 `dotnet publish` 完成后重命名 AppHost EXE。
4. 正式发布目录不得保留 `CrossETF.Terminal.UiShell.Reference.exe`。
5. 当前用户桌面快捷方式统一命名为 `跨境ETF.lnk`，并覆盖更新同一个快捷方式。
6. 快捷方式始终指向当前最新正式版本的 `跨境ETF.exe`，工作目录为对应正式发布目录，图标来自正式 EXE。
7. 发布前必须验证 detached worktree 的 HEAD 与 `ExpectedCommit` 完整哈希一致。
8. 发布后必须验证 `FileVersion`，且 `ProductVersion/InformationalVersion` 必须包含最终锁定提交哈希。
9. 默认 `Launch` 模式必须保留正式 EXE 至少运行 8 秒并正常关闭的验证；快捷方式安装后也必须验证能够启动同一 EXE。
10. 显式 `Static` 模式执行与 `Launch` 完全相同的 publish 和静态包校验，但不得启动应用 EXE，也不得通过快捷方式启动应用。
11. 快捷方式必须先在同目录临时创建并验证，再使用原子替换安装；替换后验证失败必须原子恢复原快捷方式并复核原 hash/属性。
12. publish 只写同卷 staging；静态检查通过后使用目录移动形成正式目录。同版本内容相同视为幂等成功，内容不同必须拒绝覆盖。
13. 来源必须是 clean detached worktree，并由本地和 origin 上同名 annotated tag 精确指向 `ExpectedCommit`。
14. 正式包必须校验中文 EXE、核心依赖、版本/提交、程序集名称、污染文件、EXE SHA-256 和确定性 manifest hash。
15. `artifacts` 和桌面 `.lnk` 均不得提交到 Git。
16. `v8.2.0` 及更早发布目录不追溯改名。
17. 后续正式版本必须继续使用同一脚本发布，不得手工改变程序集身份来实现中文文件名。

## 脚本参数

- `Version`：三段式版本号，例如 `8.2.1`。
- `SourcePath`（别名 `WorktreePath`）：最终标签 detached worktree 路径。
- `OutputRoot`：发布根目录，默认 `D:\shibaowangETF\artifacts\release`。
- `CreateDesktopShortcut`：验证发布 EXE 后创建或更新当前用户桌面快捷方式。
- `ExpectedCommit`：最终锁定提交的完整 40 位哈希。
- `ValidationMode`：`Launch` 或 `Static`，默认 `Launch`。`Static` 只做完整发布和静态验证，不启动任何应用 EXE。

## 使用示例

```powershell
# 默认 Launch：保留正式 EXE 和可选快捷方式启动验证
.\scripts\Publish-CrossEtfRelease.ps1 `
  -Version 8.10.3 `
  -SourcePath D:\release-worktree `
  -ExpectedCommit <40位SHA> `
  -ValidationMode Launch

# Static：完成发布和静态校验，但不启动任何应用 EXE
.\scripts\Publish-CrossEtfRelease.ps1 `
  -Version 8.10.3 `
  -SourcePath D:\release-worktree `
  -ExpectedCommit <40位SHA> `
  -ValidationMode Static
```

脚本失败时返回非零退出码，不使用强制终止进程，不用无效快捷方式覆盖原有可用快捷方式。发布工具的静态模式、staging、manifest 和快捷方式恢复口径详见 `TASK_RELEASE_STATIC_VALIDATION_029.md`。
