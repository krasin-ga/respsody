using System.Collections.Concurrent;

namespace Respsody.Resp;

public sealed class RespAggregatesPool
{
    public static readonly RespAggregatesPool Shared = new();

    //todo: use ThreadStaticPool<>
    private readonly ConcurrentQueue<RespAggregate> _queue = new();

    public RespAggregate Lease()
    {
        if (_queue.TryDequeue(out var agg))
        {
            agg.TakenFromPool();
            return agg;
        }

        return new RespAggregate(this);
    }

    public void Return(RespAggregate aggregate)
    {
       _queue.Enqueue(aggregate);
       aggregate.ReturnedToPool();
    }
}