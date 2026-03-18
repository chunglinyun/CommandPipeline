using CommandPipeline.Service;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace CommandPipeline.UnitTests;

public class OrderProcessingChainTests
{
    private readonly IMediator _mediator;
    private readonly List<string> _executionLog;

    public OrderProcessingChainTests()
    {
        // 建立測試用 mediator 與共用執行紀錄，讓各 handler 可以回寫執行順序供驗證使用。
        _executionLog = new List<string>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(OrderProcessingChainTests).Assembly));

        services.AddSingleton(_executionLog);

        var provider = services.BuildServiceProvider();
        _mediator = provider.GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task ExecuteChain_ShouldFollowStageOrderAndParallelism()
    {
        // Flow:
        // 1. 建立主鏈：Stage 1 送出三種 command 各 10 筆並行執行。
        // 2. Stage 1 完成後接 Stage 2，再於 Stage 2 後並行執行兩筆 Stage 3。
        // 3. 另外獨立送出 Stage 4 fire-and-forget 工作，不阻塞主鏈。
        // 4. 執行主鏈後驗證 Stage 1 → Stage 2 → Stage 3 的數量與順序。
        // 5. 最後等待 Stage 4 完成並驗證數量。
        var stage1Chain = CommandChain.Create(_mediator);

        for (int i = 0; i < 10; i++)
        {
            stage1Chain
                .Also(new StageOneAlphaCommand(i))
                .Also(new StageOneBetaCommand(i))
                .Also(new StageOneGammaCommand(i));
        }

        stage1Chain
            .Then(new StageTwoCommand())
            .Also(new StageThreeFirstCommand())
            .Also(new StageThreeSecondCommand())
            .Barrier();

        for (int i = 10; i < 20; i++)
        {
            stage1Chain
                .Also(new StageFourXCommand(i))
                .Also(new StageFourYCommand(i));
        }

        var ctx = await stage1Chain.ExecuteAsync(stopOnFailure: true);

        Assert.False(ctx.IsFailed, $"Chain failed: {string.Join(", ", ctx.Errors)}");

        Assert.Equal(10, _executionLog.Count(l => l.StartsWith("Stage1-Alpha")));
        Assert.Equal(10, _executionLog.Count(l => l.StartsWith("Stage1-Beta")));
        Assert.Equal(10, _executionLog.Count(l => l.StartsWith("Stage1-Gamma")));

        Assert.Single(_executionLog.Where(l => l.StartsWith("Stage2")));

        int lastStage1Index = _executionLog
            .Select((log, idx) => (log, idx))
            .Where(x => x.log.StartsWith("Stage1"))
            .Max(x => x.idx);

        int firstStage2Index = _executionLog
            .Select((log, idx) => (log, idx))
            .First(x => x.log.StartsWith("Stage2")).idx;

        Assert.True(firstStage2Index > lastStage1Index,
            "Stage2 should start after all Stage1 commands complete");

        Assert.Equal(2, _executionLog.Count(l => l.StartsWith("Stage3")));

        int stage2Index = _executionLog
            .Select((log, idx) => (log, idx))
            .First(x => x.log.StartsWith("Stage2")).idx;

        int firstStage3Index = _executionLog
            .Select((log, idx) => (log, idx))
            .Where(x => x.log.StartsWith("Stage3"))
            .Min(x => x.idx);

        Assert.True(firstStage3Index > stage2Index,
            "Stage3 commands should both start after Stage2 completes");

        int firstStage4Index = _executionLog
            .Select((log, idx) => (log, idx))
            .Where(x => x.log.StartsWith("Stage4"))
            .Min(x => x.idx);

        Assert.True(firstStage4Index > firstStage3Index,
            "Stage4 should start after both Stage3 commands complete");

        Assert.Equal(10, _executionLog.Count(l => l.StartsWith("Stage4-X")));
        Assert.Equal(10, _executionLog.Count(l => l.StartsWith("Stage4-Y")));
    }
}

// ════════════════════════════════════════════════
// Commands
// ════════════════════════════════════════════════

public record StageOneAlphaCommand(int Index) : IRequest;

public record StageOneBetaCommand(int Index) : IRequest;

public record StageOneGammaCommand(int Index) : IRequest;

public record StageTwoCommand : IRequest;

public record StageThreeFirstCommand : IRequest;

public record StageThreeSecondCommand : IRequest;

public record StageFourXCommand(int Index) : IRequest;

public record StageFourYCommand(int Index) : IRequest;

// ════════════════════════════════════════════════
// Handlers（寫入 log 供驗證用）
// ════════════════════════════════════════════════

public class StageOneAlphaHandler(List<string> log)
    : IRequestHandler<StageOneAlphaCommand>
{
    public async Task Handle(StageOneAlphaCommand req, CancellationToken ct)
    {
        await Task.Delay(10, ct);
        lock (log) log.Add($"Stage1-Alpha-{req.Index}");
    }
}

public class StageOneBetaHandler(List<string> log)
    : IRequestHandler<StageOneBetaCommand>
{
    public async Task Handle(StageOneBetaCommand req, CancellationToken ct)
    {
        await Task.Delay(10, ct);
        lock (log) log.Add($"Stage1-Beta-{req.Index}");
    }
}

public class StageOneGammaHandler(List<string> log)
    : IRequestHandler<StageOneGammaCommand>
{
    public async Task Handle(StageOneGammaCommand req, CancellationToken ct)
    {
        await Task.Delay(10, ct);
        lock (log) log.Add($"Stage1-Gamma-{req.Index}");
    }
}

public class StageTwoHandler(List<string> log)
    : IRequestHandler<StageTwoCommand>
{
    public async Task Handle(StageTwoCommand req, CancellationToken ct)
    {
        await Task.Delay(10, ct);
        lock (log) log.Add("Stage2");
    }
}

public class StageThreeFirstHandler(List<string> log)
    : IRequestHandler<StageThreeFirstCommand>
{
    public async Task Handle(StageThreeFirstCommand req, CancellationToken ct)
    {
        await Task.Delay(10, ct);
        lock (log) log.Add("Stage3-First");
    }
}

public class StageThreeSecondHandler(List<string> log)
    : IRequestHandler<StageThreeSecondCommand>
{
    public async Task Handle(StageThreeSecondCommand req, CancellationToken ct)
    {
        await Task.Delay(10, ct);
        lock (log) log.Add("Stage3-Second");
    }
}

public class StageFourXHandler(List<string> log)
    : IRequestHandler<StageFourXCommand>
{
    public async Task Handle(StageFourXCommand req, CancellationToken ct)
    {
        await Task.Delay(10, ct);
        lock (log) log.Add($"Stage4-X-{req.Index}");
    }
}

public class StageFourYHandler(List<string> log)
    : IRequestHandler<StageFourYCommand>
{
    public async Task Handle(StageFourYCommand req, CancellationToken ct)
    {
        await Task.Delay(10, ct);
        lock (log) log.Add($"Stage4-Y-{req.Index}");
    }
}