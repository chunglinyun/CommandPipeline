using CommandPipeline.Service;
using MediatR;
using Moq;

namespace CommandPipeline.UnitTests;

public class CommandChainTests
{
    [Fact]
    public async Task ExecuteAsync_WhenStopOnFailureIsTrue_StopsBeforeNextSequentialCommand()
    {
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var failing = new FailingCommand("fail");
        var next = new NoopCommand("next");
        var ex = new InvalidOperationException("boom");

        mediator
            .Setup(m => m.Send(It.Is<IRequest>(r => ReferenceEquals(r, failing)), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

        var chain = CommandChain.Create(mediator.Object)
            .Then(failing)
            .Then(next);

        var ctx = await chain.ExecuteAsync();

        Assert.True(ctx.IsFailed);
        Assert.Single(ctx.Errors);
        Assert.Same(ex, ctx.Errors[0]);

        mediator.Verify(m => m.Send(It.Is<IRequest>(r => ReferenceEquals(r, failing)), It.IsAny<CancellationToken>()), Times.Once);
        mediator.Verify(m => m.Send(It.Is<IRequest>(r => ReferenceEquals(r, next)), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStopOnFailureIsFalse_KeepsFailureStateWithoutThrowing()
    {
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var failing = new FailingCommand("fail");
        var next = new NoopCommand("next");

        mediator
            .Setup(m => m.Send(It.Is<IRequest>(r => ReferenceEquals(r, failing)), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var chain = CommandChain.Create(mediator.Object)
            .Then(failing)
            .Then(next);

        var ctx = await chain.ExecuteAsync(stopOnFailure: false);

        Assert.True(ctx.IsFailed);
        Assert.Single(ctx.Errors);
        mediator.Verify(m => m.Send(It.Is<IRequest>(r => ReferenceEquals(r, failing)), It.IsAny<CancellationToken>()), Times.Once);
        mediator.Verify(m => m.Send(It.Is<IRequest>(r => ReferenceEquals(r, next)), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_FlushesPendingParallelCommandsAtEndOfChain()
    {
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var first = new NoopCommand("p1");
        var second = new NoopCommand("p2");
        var token = new CancellationTokenSource().Token;

        mediator.Setup(m => m.Send(It.Is<object>(r => ReferenceEquals(r, first)), token)).ReturnsAsync((object?)null);
        mediator.Setup(m => m.Send(It.Is<object>(r => ReferenceEquals(r, second)), token)).ReturnsAsync((object?)null);

        var chain = CommandChain.Create(mediator.Object)
            .Also(first)
            .Also(second);

        await chain.ExecuteAsync(token);

        mediator.Verify(m => m.Send(It.Is<object>(r => ReferenceEquals(r, first)), token), Times.Once);
        mediator.Verify(m => m.Send(It.Is<object>(r => ReferenceEquals(r, second)), token), Times.Once);
    }

    [Fact]
    public async Task ThenWithResult_StoresValueInContext_WhenResultKeyProvided()
    {
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var query = new ResultCommand("result");
        var token = new CancellationTokenSource().Token;

        mediator
            .Setup(m => m.Send(query, token))
            .ReturnsAsync("done");

        var chain = CommandChain.Create(mediator.Object)
            .Then<ResultCommand, string>(query, "answer");

        var ctx = await chain.ExecuteAsync(token);

        Assert.Equal("done", ctx.Get<string>("answer"));
        mediator.Verify(m => m.Send(query, token), Times.Once);
    }

    [Fact]
    public async Task AlsoCommands_RunBeforeFollowingThenStage()
    {
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var first = new NoopCommand("p1");
        var second = new NoopCommand("p2");
        var last = new NoopCommand("s1");
        var executionOrder = new List<string>();

        mediator
            .Setup(m => m.Send(It.Is<object>(r => ReferenceEquals(r, first)), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add(first.Id))
            .ReturnsAsync((object?)null);
        mediator
            .Setup(m => m.Send(It.Is<object>(r => ReferenceEquals(r, second)), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add(second.Id))
            .ReturnsAsync((object?)null);
        mediator
            .Setup(m => m.Send(It.Is<IRequest>(r => ReferenceEquals(r, last)), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add(last.Id))
            .Returns(Task.CompletedTask);

        var chain = CommandChain.Create(mediator.Object)
            .Also(first)
            .Also(second)
            .Then(last);

        await chain.ExecuteAsync();

        Assert.Equal(3, executionOrder.Count);
        Assert.Equal(last.Id, executionOrder[^1]);
    }

    private sealed record NoopCommand(string Id) : IRequest;
    private sealed record FailingCommand(string Id) : IRequest;
    private sealed record ResultCommand(string Id) : IRequest<string>;
}
