---
phase: 1
title: "Thuộc tính hành vi trên definition, catalog suy ra, test khoá"
status: completed
priority: P1
dependencies: []
---

# Phase 1: Thuộc tính hành vi trên definition

## Overview

Thêm `KeepsAuditPayload`, `AffectsOutputs`, `UsesOutputLane` vào `CommandDefinition`; `PinnedCommandCatalog` gán giá trị. Chưa nơi nào đọc, nên phase này không đổi hành vi.

## Requirements

- Functional: mỗi definition từ catalog mang đúng ba cờ theo bảng ở `plan.md`.
- Non-functional: mặc định `false`; catalog JSON, hash, số row không đổi; không thêm trường vào response API.

## Architecture

```csharp
// CommandDefinition: thêm cuối danh sách tham số, sau DryRunSupported
bool KeepsAuditPayload = false,
bool AffectsOutputs = false,
bool UsesOutputLane = false
```

```csharp
// PinnedCommandCatalog.ToDefinition
var key = (row.Type, row.Action);
var affectsOutputs = commandClass == CommandClass.Physical && row.Type == "control";
var isRelay = affectsOutputs && PhysicalOutputActions.Contains(key);
KeepsAuditPayload: isRelay || ConfigWriteActions.Contains(key) || FlashActions.Contains(key),
AffectsOutputs: affectsOutputs,
UsesOutputLane: isRelay
```

`isRelay` cần điều kiện `type`: `PhysicalOutputActions` cũng là danh sách mở row class B, nên không được dùng một mình để suy hành vi.

`KeepsAuditPayload` cố ý không dùng `opened`: action Read mở không lưu payload (giữ đúng hành vi hiện tại).

## Related Code Files

- Modify: `src/Hms.Server.Domain/Commands/CommandDefinition.cs`
- Modify: `src/Hms.Server.Infrastructure/Commands/PinnedCommandCatalog.cs`
- Modify: `tests/Hms.Server.UnitTests/CatalogTests.cs`

## Implementation Steps (tests first)

1. Chạy baseline: `dotnet test Hms.Server.slnx`, ghi số test pass.
2. Viết test đỏ trong `CatalogTests.cs`:
   - Bảng kỳ vọng tường minh cho cả 15 row B/D/F (`type.action` → ba cờ). Test duyệt mọi definition class B/D/F trong catalog thật: row không có trong bảng → fail; giá trị lệch → fail; bảng có row không còn trong catalog → fail.

     | Action | Keeps | Affects | OutputLane |
     |---|---|---|---|
     | `control.set_output`, `control.pulse` | true | true | true |
     | `control.set_all`, `control.force_set`, `control.force_clear`, `control.set_config` | false | true | false |
     | `sms.send` | false | false | false |
     | 4 row D | false | false | false |
     | `system.reboot` | true | false | false |
     | `system.factory_reset`, `ota.apply`, `ota.remote_start` | false | false | false |
   - 5 action `ConfigWriteActions`: `KeepsAuditPayload == true`, hai cờ còn lại `false`.
   - Mọi row class R: cả ba cờ `false`.
   - Bất biến: `UsesOutputLane` ⇒ class B và `AffectsOutputs`; `KeepsAuditPayload` ⇒ `Availability == Enabled`.
   - Mọi phần tử của `PhysicalOutputActions` có `Type == "control"`.
3. Thêm ba tham số vào record, cập nhật XML doc: mỗi cờ một câu nói rõ ai đọc và vì sao mặc định `false`.
4. Gán trong `ToDefinition`. Sửa comment trên `PhysicalOutputActions` (dòng 84-87): danh sách này mở row class B; hành vi relay (lane output, lưu payload) chỉ áp dụng cho row `control` trong danh sách.
5. Chạy lại toàn bộ test: số pass = baseline + test mới; `node scripts/generate-command-catalog.mjs --check` sạch; `git diff --stat` không có `command-catalog.json`.

## Success Criteria

- [x] Test mới xanh; test cũ không sửa dòng nào.
- [x] `command-catalog.json` không đổi; test hash/pair-set hiện có xanh.
- [x] Test contract `/me/command-catalog` hiện có xanh (DTO map tay ở `MeController.cs:60-71`, không cần sửa).

## Risk Assessment

- Tham số positional mới làm lệch lời gọi cũ: thêm ở cuối, có default → không lệch. Compiler xác nhận.
- Bảng kỳ vọng trong test trùng logic catalog: chấp nhận, đó là mục đích (khai báo hai nơi, lệch là đỏ).
