using CommandPipeline.Core;

namespace CommandPipeline.Abstractions;

public interface IChainStage
{
    Task ExecuteAsync(ChainContext ctx);
}
