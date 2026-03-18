using MediatR;
using CommandPipeline.Abstractions;
using CommandPipeline.Core;

namespace CommandPipeline.Stages;

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
