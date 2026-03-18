using MediatR;
using CommandPipeline.Abstractions;
using CommandPipeline.Core;

namespace CommandPipeline.Stages;

// 無順序：與其他 Parallel Stage 一起跑
public class ParallelStage(IEnumerable<IBaseRequest> commands, IMediator mediator) : IChainStage
{
    public async Task ExecuteAsync(ChainContext ctx)
    {
        var tasks = commands.Select(async cmd =>
        {
            try
            {
                await mediator.Send(cmd, ctx.CancellationToken);
            }
            catch (Exception ex)
            {
                ctx.Fail(ex);
            }
        });
        await Task.WhenAll(tasks);
    }
}
