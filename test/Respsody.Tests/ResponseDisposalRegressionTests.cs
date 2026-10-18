using System;
using Respsody.Client;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class ResponseDisposalRegressionTests
{
    [Fact]
    public void ScalarReadersRejectDisposedMemory()
    {
        var number = new RespNumber(RegressionFrames.Create(":1\r\n"), new CompletionGuard());
        var big = new RespBigNumber(RegressionFrames.Create("(1\r\n"), new CompletionGuard());
        var real = new RespDouble(RegressionFrames.Create(",1\r\n"), new CompletionGuard());
        var boolean = new RespBoolean(RegressionFrames.Create("#t\r\n"), new CompletionGuard());
        number.Dispose();
        big.Dispose();
        real.Dispose();
        boolean.Dispose();
        Assert.Throws<ObjectDisposedException>(() => number.ToInt64());
        Assert.Throws<ObjectDisposedException>(() => big.ToBigInteger());
        Assert.Throws<ObjectDisposedException>(() => real.ToDouble());
        Assert.Throws<ObjectDisposedException>(() => boolean.ToBool());
    }

    [Theory]
    [InlineData("+text\r\n", 0, 1)]
    [InlineData("_\r\n", 0, 1)]
    [InlineData("$-1\r\n", -1, 5)]
    [InlineData("=8\r\ntxt:text\r\n", 8, 4)]
    public void StringReadersAndVerbatimFormatRejectDisposedMemory(string value, int length, int offset)
    {
        var response = new RespString(RegressionFrames.Create(value, length: length, dataOffset: offset), new CompletionGuard());
        response.Dispose();
        Assert.Throws<ObjectDisposedException>(() => response.ToString());
        Assert.Throws<ObjectDisposedException>(() => response.GetSpan().Length);
        Assert.Throws<ObjectDisposedException>(() => response.AsUnicodeSpan().Length);
        Assert.Throws<ObjectDisposedException>(() => response.IsNull());
        Assert.Throws<ObjectDisposedException>(() => response.TryGetVerbatimFormat(out _));
    }

    [Fact]
    public void CollectionReadersRejectDisposedAndReusedAggregates()
    {
        var pool = new RespAggregatesPool();
        var aggregate = pool.Lease();
        aggregate.Add(RegressionFrames.Create("*1\r\n", length: 1));
        aggregate.Add(RegressionFrames.Create("+old\r\n"));
        var array = new RespArray(aggregate, new CompletionGuard());
        var child = array[0].ToRespString();
        var enumerator = array.EnumerateStrings();
        array.Dispose();
        using var reused = pool.Lease();
        Assert.Same(aggregate, reused);
        reused.Add(RegressionFrames.Create("*1\r\n", length: 1));
        reused.Add(RegressionFrames.Create("+new\r\n"));
        Assert.Throws<ObjectDisposedException>(() => array[0]);
        Assert.Throws<ObjectDisposedException>(() => child.ToString());
        Assert.Throws<ObjectDisposedException>(() => enumerator.MoveNext());

        var setAggregate = pool.Lease();
        setAggregate.Add(RegressionFrames.Create("~0\r\n"));
        var set = new RespSet(setAggregate, new CompletionGuard());
        set.Dispose();
        Assert.Throws<ObjectDisposedException>(() => set[0]);
        Assert.Throws<ObjectDisposedException>(() => set.ToArrayOf(static (in OwnedRespValueVariant value) => 0));
        Assert.Throws<ObjectDisposedException>(() => set.ToHashSetOf(static (in OwnedRespValueVariant value) => 0));

        var mapAggregate = pool.Lease();
        mapAggregate.Add(RegressionFrames.Create("%0\r\n"));
        var map = new RespMap(mapAggregate, new CompletionGuard());
        map.Dispose();
        Assert.Throws<ObjectDisposedException>(() => map.ToMapWithStringKey());

        var pushAggregate = pool.Lease();
        pushAggregate.Add(RegressionFrames.Create(">0\r\n"));
        var push = new RespPush(pushAggregate, new CompletionGuard());
        push.Dispose();
        Assert.Throws<ObjectDisposedException>(() => push[0]);
        Assert.Throws<ObjectDisposedException>(() => push.ToArrayOf(static (in OwnedRespValueVariant value) => 0));
    }
}
