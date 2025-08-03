using System.Diagnostics;
using Respsody.Memory;

namespace Respsody.Resp;

[DebuggerDisplay("{ToDebugString()}")]
public readonly struct RespValueVariant : IDisposable
{
    public RespAggregate? Aggregate { get; }
    public Frame<RespContext>? Simple { get; }

    public RespType Type => Simple is { } simple
        ? simple.Context.Type
        : Aggregate!.Context.Type;

    public RespValueVariant(RespAggregate aggregate)
    {
        Aggregate = aggregate;
        Simple = null;
    }

    public RespValueVariant(in Frame<RespContext> simple)
    {
        Simple = simple;
        Aggregate = null;
    }

    public void Dispose()
    {
        if (Simple is { })
        {
            Simple.Value.Dispose();
            return;
        }

        Aggregate?.Dispose();
    }

    public string ToDebugString()
    {
        if (Aggregate is { })
            return $"[{Aggregate.ToDebugString()}]";

        return Simple!.Value.ToDebugString();
    }
}