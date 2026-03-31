namespace Buffero.App.Infrastructure;

internal sealed class SingleItemBuffer<T> : IDisposable
    where T : class
{
    private readonly object _gate = new();
    private T? _item;

    public T? Take()
    {
        lock (_gate)
        {
            var item = _item;
            _item = null;
            return item;
        }
    }

    public void Store(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        T? displacedItem;
        lock (_gate)
        {
            displacedItem = _item;
            _item = item;
        }

        DisposeIfNeeded(displacedItem);
    }

    public void Clear()
    {
        T? itemToDispose;
        lock (_gate)
        {
            itemToDispose = _item;
            _item = null;
        }

        DisposeIfNeeded(itemToDispose);
    }

    public void Dispose()
    {
        Clear();
    }

    private static void DisposeIfNeeded(T? item)
    {
        if (item is IDisposable disposableItem)
        {
            disposableItem.Dispose();
        }
    }
}
