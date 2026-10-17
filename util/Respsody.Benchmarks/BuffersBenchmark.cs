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
    private byte[] _testDataToCopy = null!;
    private static int _flag;

    [Params(256, 4096, 256 * 1024)]
    public int MessageSize { get; set; } = 256 * 1024;

    [GlobalSetup]
    public void Setup()
    {
        _testDataToCopy = Encoding.UTF8.GetBytes(new string('1', MessageSize));
    }

    [Benchmark]
    public int StackOrPool()
    {
        byte[]? pooled = null;
        Span<byte> buffer = _testDataToCopy.Length <= 4096
            ? stackalloc byte[_testDataToCopy.Length]
            : (pooled = ArrayPool<byte>.Shared.Rent(_testDataToCopy.Length));

        try
        {
            _testDataToCopy.AsSpan().CopyTo(buffer);
            return _testDataToCopy.Length;
        }
        finally
        {
            if (pooled is not null)
                ArrayPool<byte>.Shared.Return(pooled);
        }
    }

    [Benchmark]
    public int StaticWithInterlocked()
    {
        var spinner = new SpinWait();
        while (Interlocked.CompareExchange(ref _flag, 1, 0) != 0)
            spinner.SpinOnce();

        try
        {
            _testDataToCopy.AsSpan().CopyTo(Buffer2);
            return _testDataToCopy.Length;
        }
        finally
        {
            Volatile.Write(ref _flag, 0);
        }
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
