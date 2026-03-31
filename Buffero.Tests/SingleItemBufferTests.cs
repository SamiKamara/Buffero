using Buffero.App.Infrastructure;

namespace Buffero.Tests;

public sealed class SingleItemBufferTests
{
    [Fact]
    public void Take_ReturnsStoredInstance_AndClearsTheBuffer()
    {
        using var buffer = new SingleItemBuffer<DisposableProbe>();
        var probe = new DisposableProbe();

        buffer.Store(probe);

        var firstTake = buffer.Take();
        var secondTake = buffer.Take();

        Assert.Same(probe, firstTake);
        Assert.Null(secondTake);
        Assert.False(probe.IsDisposed);
    }

    [Fact]
    public void Store_DisposesPreviousInstance_WhenReplacingBufferedItem()
    {
        using var buffer = new SingleItemBuffer<DisposableProbe>();
        var firstProbe = new DisposableProbe();
        var secondProbe = new DisposableProbe();

        buffer.Store(firstProbe);
        buffer.Store(secondProbe);

        Assert.True(firstProbe.IsDisposed);
        Assert.False(secondProbe.IsDisposed);
        Assert.Same(secondProbe, buffer.Take());
    }

    [Fact]
    public void Dispose_DisposesBufferedInstance()
    {
        var buffer = new SingleItemBuffer<DisposableProbe>();
        var probe = new DisposableProbe();

        buffer.Store(probe);
        buffer.Dispose();

        Assert.True(probe.IsDisposed);
        Assert.Null(buffer.Take());
    }

    private sealed class DisposableProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
