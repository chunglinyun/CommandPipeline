using MediatR;
using CommandPipeline.Abstractions;
using CommandPipeline.Core;

namespace CommandPipeline.Stages;

// 有順序：等前一個完成才執行
public class SequentialStage(IRequest command, IMediator mediator) : IChainStage
{
    public async Task ExecuteAsync(ChainContext ctx)
    {
        if (ctx.IsFailed) return;
        try
        {
            await mediator.Send(command, ctx.CancellationToken);
        }
        catch (Exception ex)
        {
            ctx.Fail(ex);
        }
    }
}
