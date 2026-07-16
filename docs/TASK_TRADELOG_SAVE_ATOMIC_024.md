# TASK-TRADELOG-SAVE-ATOMIC-024

## Scope

V8.10.1 addresses four P0 save-path risks without changing TradeLog fields, account replay formulas, strategy decisions, order drafts, UI layout, or the SQLite schema:

1. TradeLog facts and core replay state previously committed in separate transactions.
2. Rapid repeated save activation could reenter the asynchronous save path.
3. A successful database commit followed by a UI synchronization failure could leave the same new row eligible for another INSERT.
4. Save errors did not clearly distinguish rollback from a successful commit followed by UI failure.

The reported duplicate row was later fully edited into a real trade on the next day. There is no current evidence of damaged production facts, so this task does not inspect, merge, delete, or repair historical TradeLog records.

## Fact-source contract

- `trade_log` remains the only execution fact source.
- Only the existing manual TradeLog save action writes TradeLog facts.
- Two intentionally identical trades remain two legal facts with distinct database IDs.
- There is no content hash deduplication, date/code/quantity uniqueness rule, automatic merge, or historical cleanup.

## Atomic transaction

The dedicated repository entry uses one SQLite connection and one transaction. The transaction covers exactly these core tables:

1. `trade_log`
2. `account_replay_state`
3. `account_replay_snapshot`
4. `position_replay_state`
5. `otc_position_replay_state`

Within that transaction the repository:

1. Applies explicit deletes.
2. Updates rows with existing IDs and verifies exactly one row was affected.
3. Inserts new rows and captures `last_insert_rowid()` on the immutable save clones.
4. Reads the final uncommitted TradeLog set through the same connection and transaction.
5. Runs the unchanged `AccountReplayService` after final IDs have been assigned, preserving the existing `Time -> Id -> RowIndex` ordering.
6. Persists the account state, account snapshot, ETF positions, and OTC positions through the same transaction.
7. Commits once after every core write succeeds.
8. Returns snapshot-indexed ID mappings only after commit succeeds.

The core transaction does not perform network access, read external files, call strategy or order-draft services, or write `runtime_log`.

## Replay status behavior

- `正常`: commit all five core tables.
- `估值不完整`: commit all five core tables and retain the incomplete valuation status.
- `财务异常`: throw a controlled failure and roll back all five core tables.

Financial replay formulas, action meanings, cash/principal calculations, position calculations, and ordering rules are unchanged.

## ID and retry safety

- Identity mapping uses `SnapshotIndex`, `OriginalId`, and `PersistedId`; it never matches rows by trade content.
- New IDs are assigned inside the transaction to save clones and returned only after commit.
- Existing rows retain their IDs.
- Deleted rows do not appear in the returned mapping.
- Failed transactions restore transient clone IDs and can be retried safely.
- UPDATE or DELETE of a missing expected ID fails the whole transaction; UPDATE never falls back to INSERT.

## UI reentry and commit-state handling

- The save guard is set before the first asynchronous boundary.
- While saving, the TradeLog save button is disabled and repeated mouse, keyboard, or asynchronous activation exits before cloning, calculation, or repository access.
- A rollback displays: `保存未完成，数据库已保持保存前状态。可以检查输入后重新保存。`
- A financial replay rollback displays: `保存未完成：账户回放检测到财务异常，数据库已保持保存前状态。`
- After a successful commit, IDs and calculated values are synchronized back to the bound rows and local data is reloaded strictly.
- If any post-commit UI synchronization or notification fails, the window displays: `交易事实和账户状态已经保存成功，但界面同步失败。请关闭并重新打开交易日志窗口，不要再次点击保存。`
- In that state the save button remains disabled and the same window cannot call the repository again. Closing and reopening performs the normal local read of committed data.
- `DataSaved` is raised only after the core commit and UI synchronization succeed.

## Deterministic fault testing

The repository exposes an internal test-only fault callback. Production callers always pass `null`; there is no random production fault, environment-variable switch, or user-accessible fault control.

Temporary SQLite tests inject failures before and after TradeLog writes, before and after replay, after each replay table phase, before commit, and in the commit action. Every injected failure verifies that the deterministic logical hash of all five core tables remains unchanged.

Additional tests cover:

- normal and incomplete-valuation commit;
- financial-error rollback;
- new/existing/deleted ID mapping;
- legal identical trades;
- rapid double-save and keyboard-equivalent reentry guard structure;
- post-commit UI synchronization failure;
- missing UPDATE/DELETE targets;
- rollback then insert/update/delete retry;
- same-second final-ID replay order;
- mixed CASH funding/withdrawal, ETF BUY/SELL, OTC substitute BUY/SELL, fees, and multiple strategies;
- 100, 500, and 1000 row transaction completion without an unreliable absolute timing threshold.

All automated tests use random temporary SQLite files. They do not start the full application executable, access the production database, read the user's TradeLog, or request live market data.

## Manual acceptance

Use only the isolated V8.10.1 test package. Verify:

1. A normal new row adds exactly one database record.
2. Rapidly clicking save twice still adds exactly one record and updates account state once.
3. Two intentionally identical rows both save with different IDs.
4. Editing an existing row keeps its ID and does not insert a new row.
5. Deleting a middle row rebuilds account and positions from the remaining facts.
6. A controlled financial error leaves the database unchanged and succeeds after the input is corrected.
7. Layout, column order, dark theme, title-bar behavior, strategy, drafts, and charts remain unchanged.

This test cycle does not commit, push, create a V8.10.1 tag, replace the formal V8.10.0 package, or update the desktop shortcut.
