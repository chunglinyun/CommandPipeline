# CommandPipeline

`CommandPipeline` 是一個基於 **MediatR** 的命令編排工具，讓你可以用 fluent 方式把多個 `IRequest` 串成「有順序（Sequential）」與「可併行（Parallel）」的執行流程。

目前專案型態為 `.NET 8` class library，核心入口是 `CommandChain`。

## 功能特色

- 以 `CommandChain.Create(mediator)` 建立可鏈式組裝的命令流程
- `Then(...)`：有順序執行，會先 flush 目前暫存的平行群組
- `Also(...)`：加入平行群組（延後到下一次 flush 或 `ExecuteAsync` 時執行）
- `Then<TCommand, TResult>(..., resultKey)`：執行有回傳值命令並可寫入 `ChainContext`
- 內建失敗收斂：例外會被收集到 `ChainContext.Errors` 並標記 `IsFailed`
- 支援 `CancellationToken`，由 `ExecuteAsync(ct)` 傳遞到每個 MediatR `Send`

## 環境需求

- .NET SDK `8.0.x`（參考 `global.json`，設定為 `8.0.0` 並 `rollForward: latestMinor`）
- NuGet 套件：`MediatR` `14.1.0`

## 安裝 / 引用

目前此專案看起來是原始碼型態（尚未看到 NuGet package 設定），建議以下方式使用：

1. 將 repository clone 到本機
2. 在你的解決方案中加入 `CommandPipeline/CommandPipeline.csproj` 專案參考

例如（在你的 app 專案 `.csproj`）：

```xml
<ItemGroup>
  <ProjectReference Include="..\CommandPipeline\CommandPipeline\CommandPipeline.csproj" />
</ItemGroup>
```

## 快速開始

> 以下示範重點在 `CommandChain` 使用方式；`IRequest`/Handler 註冊請依你的 MediatR 專案設定。

```csharp
using MediatR;
using CommandPipeline.Orchestration;

// 範例命令（實際請在你的應用程式中實作）
public record CreateOrderCommand(int OrderId) : IRequest;
public record ChargePaymentCommand(int OrderId) : IRequest;
public record ReserveStockCommand(int OrderId) : IRequest;
public record GetOrderTotalCommand(int OrderId) : IRequest<decimal>;

public static async Task DemoAsync(IMediator mediator, CancellationToken ct)
{
    var ctx = await CommandChain
        .Create(mediator)
        .Then(new CreateOrderCommand(1001))
        .Also(new ChargePaymentCommand(1001))
        .Also(new ReserveStockCommand(1001))
        .Then<GetOrderTotalCommand, decimal>(
            new GetOrderTotalCommand(1001),
            resultKey: "order.total")
        .ExecuteAsync(ct, stopOnFailure: true);

    if (ctx.IsFailed)
    {
        foreach (var error in ctx.Errors)
        {
            Console.WriteLine(error.Message);
        }
        return;
    }

    var total = ctx.Get<decimal>("order.total");
    Console.WriteLine($"Order total: {total}");
}
```

## 執行模型

`CommandChain` 內部維護兩種集合：

- `_stages`：最終要依序執行的 stage 清單
- `_pendingParallel`：暫存中的平行命令群組

規則如下：

- 呼叫 `Then(...)` 時，會先把 `_pendingParallel` flush 成一個 `ParallelStage`，再新增 `SequentialStage`
- 呼叫 `Also(...)` 時，只會把命令加入 `_pendingParallel`
- 呼叫 `ExecuteAsync(...)` 前會再做一次 flush，確保最後一段平行命令也會被執行

## API 重點

### `CommandChain`

檔案：`CommandPipeline/Orchestration/CommandChain.cs`

- `Create(IMediator mediator)`：建立 chain
- `Then<TCommand>(TCommand command) where TCommand : IRequest`
- `Also<TCommand>(TCommand command) where TCommand : IRequest`
- `Then<TCommand, TResult>(TCommand command, string? resultKey = null) where TCommand : IRequest<TResult>`
- `ExecuteAsync(CancellationToken ct = default, bool stopOnFailure = true)`

### `ChainContext`

檔案：`CommandPipeline/Core/ChainContext.cs`

- `IsFailed`：是否曾有任一 stage 失敗
- `Errors`：收集所有發生的例外
- `CancellationToken`：整條 chain 使用的取消權杖
- `Set<T>(string key, T value)` / `Get<T>(string key)`：跨 stage 傳遞資料
- `Fail(Exception ex)`：標記失敗並記錄例外

### Stages

檔案：`CommandPipeline/Stages/*`

- `SequentialStage`：逐一執行單一 `IRequest`
- `SequentialStageWithResult<TResult>`：執行 `IRequest<TResult>`，可把結果存進 `ChainContext`
- `ParallelStage`：以 `Task.WhenAll` 併行送出多個 `IRequest`

## 失敗與中斷行為

- 各 stage 內部捕捉例外後會呼叫 `ctx.Fail(ex)`，不會直接將例外往外拋
- `stopOnFailure: true`（預設）時，主迴圈在偵測 `ctx.IsFailed` 後會停止後續 stage
- `stopOnFailure: false` 時，即使先前 stage 失敗，後續 stage 仍會繼續嘗試執行
- 平行群組內若多個命令都失敗，`Errors` 可能累積多筆例外

## 專案結構

```text
CommandPipeline/
  CommandPipeline.sln
  global.json
  CommandPipeline/
    CommandPipeline.csproj
    Abstractions/
      IChainStage.cs
    Core/
      ChainContext.cs
    Orchestration/
      CommandChain.cs
    Stages/
      ParallelStage.cs
      SequentialStage.cs
      SequentialStageWithResult.cs
```

## 開發指令

在 repository root 執行：

```powershell
dotnet restore
dotnet build CommandPipeline.sln
```

## 已知限制（依目前程式碼）

- 尚未包含重試策略（retry）或 fallback 機制
- 尚未內建 logging/tracing hooks
- `ChainContext` 以 `Dictionary<string, object?>` 儲存資料，key 型別安全由呼叫端自行維護
- 套件發佈（NuGet metadata/pack 流程）未在目前專案檔中看到

## 授權

目前 repository 未提供授權條款檔案（例如 `LICENSE`）。若要對外釋出，建議補上授權資訊。
