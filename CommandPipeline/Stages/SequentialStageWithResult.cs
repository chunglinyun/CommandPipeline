using MediatR;
using CommandPipeline.Abstractions;
using CommandPipeline.Core;

namespace CommandPipeline.Stages;

public class SequentialStageWithResult<TResult>(
    IRequest<TResult> command,
    IMediator mediator,
    string? resultKey) : IChainStage
{
    public async Task ExecuteAsync(ChainContext ctx)
    {
        if (ctx.IsFailed) return;
        try
        {
            var result = await mediator.Send(command, ctx.CancellationToken);
            if (resultKey is not null)
            {
                ctx.Set(resultKey, result);
            }
        }
        catch (Exception ex)
        {
            ctx.Fail(ex);
        }
    }
}
