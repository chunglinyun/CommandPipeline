using CommandPipeline.Abstractions;
using CommandPipeline.Core;
using CommandPipeline.Stages;
using MediatR;

namespace CommandPipeline.Service;

public class CommandChain
{
    private readonly IMediator _mediator;
    private readonly List<IChainStage> _stages = new();
    private readonly List<IBaseRequest> _pendingParallel = new();

    public static CommandChain Create(IMediator mediator) => new(mediator);
    public CommandChain(IMediator mediator) => _mediator = mediator;

    // 有順序命令：先把暫存的 Parallel group flush 掉
    public CommandChain Then<TCommand>(TCommand command)
        where TCommand : IRequest
    {
        FlushParallel();
        _stages.Add(new SequentialStage(command, _mediator));
        return this;
    }

    // 無順序命令：暫存到 pending group
    public CommandChain Also<TCommand>(TCommand command)
        where TCommand : IRequest
    {
        _pendingParallel.Add(command);
        return this;
    }

    // 有 result 的有順序命令（可把結果存進 Context）
    public CommandChain Then<TCommand, TResult>(
        TCommand command,
        string? resultKey = null)
        where TCommand : IRequest<TResult>
    {
        FlushParallel();
        _stages.Add(new SequentialStageWithResult<TResult>(
            command, _mediator, resultKey));
        return this;
    }

    // 結束 Parallel group，開始下一段 Sequential
    private void FlushParallel()
    {
        if (_pendingParallel.Count == 0) return;
        _stages.Add(new ParallelStage([.._pendingParallel], _mediator));
        _pendingParallel.Clear();
    }

    public async Task<ChainContext> ExecuteAsync(
        CancellationToken ct = default,
        bool stopOnFailure = true)
    {
        FlushParallel(); // 確保最後的 parallel group 也被加進來

        var ctx = new ChainContext { CancellationToken = ct };

        foreach (var stage in _stages)
        {
            if (ctx.IsFailed && stopOnFailure) break;
            await stage.ExecuteAsync(ctx);
        }

        return ctx;
    }
    
    // 強制封裝目前暫存的 parallel group
    public CommandChain Barrier()
    {
        FlushParallel(); 
        return this;
    }
}
