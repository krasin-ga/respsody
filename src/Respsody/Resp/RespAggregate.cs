using System.Diagnostics;
using Respsody.Library.Disposables;
using Respsody.Memory;

namespace Respsody.Resp;

[DebuggerDisplay("{ToDebugString()}")]
public sealed class RespAggregate : IDisposable
{
    private readonly Lifetime _lifetime;
    private readonly CompositeDisposable _owned = new();
    private bool _ownedExternally;
    private List<RespValueVariant> Elements { get; } = new(16);

    public int Length => Elements.Count;

    public Frame<RespContext> HeaderFrame { get; private set; }
    public RespContext Context => HeaderFrame.Context;

    public RespValueVariant this[int i] => Elements[i];

    public RespAggregate(RespAggregatesPool pool)
    {
        _lifetime = new Lifetime(pool, this);
    }

    public List<RespValueVariant>.Enumerator GetEnumerator() => Elements.GetEnumerator();

    public void Dispose()
    {
        if (_ownedExternally)
            return;

        _lifetime.Dispose();
    }

    public void Add(in Frame<RespContext> frame)
    {
        var (ownedFrame, lifetime) = frame.AsExternallyOwned();

        if (Elements.Count == 0)
            HeaderFrame = ownedFrame;

        Elements.Add(new RespValueVariant(ownedFrame));
        _owned.Add(lifetime);
    }

    public void Add(in RespValueVariant variant)
    {
        Debug.Assert(variant.Simple is null || Elements.Count > 0);

        if (variant.Simple is { } simple)
        {
            var (ownedFrame, lifetime) = simple.AsExternallyOwned();
            Elements.Add(new RespValueVariant(ownedFrame));
            _owned.Add(lifetime);
            return;
        }

        var agg = variant.Aggregate!;
        _owned.Add(agg.AsExternallyOwned());
        Elements.Add(new RespValueVariant(agg));
    }

    internal IDisposable AsExternallyOwned()
    {
        if (_ownedExternally)
            throw new InvalidOperationException("Ownership already transferred");

        _ownedExternally = true;
        return _lifetime;
    }

    public string ToDebugString()
    {
        const int limit = 20;
        if (Elements.Count > limit)
            return string.Join(", ", Elements.Take(limit / 20).Select(s => s.ToDebugString()))
                   + " ... "
                   + string.Join(", ", Elements.Skip(Elements.Count - limit / 2).Select(s => s.ToDebugString()));

        return string.Join(", ", Elements.Select(s => s.ToDebugString()));
    }

    private class Lifetime(RespAggregatesPool pool, RespAggregate parent) : IDisposable
    {
        public void Dispose()
        {
            parent.HeaderFrame = default;
            parent._owned.Dispose();
            parent.Elements.Clear();
            parent._ownedExternally = false;
            pool.Return(parent);
        }
    }
}