using System.Buffers;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace Respsody.Benchmarks;

[MemoryDiagnoser, ThreadingDiagnoser, OperationsPerSecond]
public class BuffersBenchmark
{
    [ThreadStatic]
    private static byte[]? _buffer;
    private static readonly byte[]? Buffer2 = new byte[1024*1024];
    private readonly byte[] _testDataToCopy = Encoding.UTF8.GetBytes(new string('1', 256*1024));
    private static int _flag;

    [Benchmark]
    public int StackAlloc()
    {
        Span<byte> buffer = stackalloc byte[_testDataToCopy.Length];

        _testDataToCopy.AsSpan().CopyTo(buffer);
        return _testDataToCopy.Length;
    }

    [Benchmark]
    public int StaticWithInterlocked()
    {
        Interlocked.CompareExchange(ref _flag, 1, 0);

        _testDataToCopy.AsSpan().CopyTo(Buffer2);

        Interlocked.Exchange(ref _flag, 0);

        return _testDataToCopy.Length;
    }

    [Benchmark]
    public int WithArrayPool()
    {
        var pooled = ArrayPool<byte>.Shared.Rent(_testDataToCopy.Length);

        _testDataToCopy.AsSpan().CopyTo(pooled);

        ArrayPool<byte>.Shared.Return(pooled);

        return _testDataToCopy.Length;
    }

    [Benchmark]
    public int ThreadStatic()
    {
        var buffer = _buffer ??= new byte[1024 * 1024];

        _testDataToCopy.AsSpan().CopyTo(buffer);
        return _testDataToCopy.Length;
    }
}