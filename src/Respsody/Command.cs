using System.Text;
using Respsody.Library;
using Respsody.Memory;
using Respsody.Resp;

namespace Respsody;

/// <summary>
/// RESP command.
/// A Command instance should not be modified after being sent once.
/// </summary>
/// <typeparam name="T">The type of the response.</typeparam>
public sealed class Command<T> : IDisposable
    where T : IRespResponse
{
    private readonly List<ushort> _slots = [];
    private bool _clusterMode;
    private bool _finalized;
    private int _length;

    private int _owners;
    private bool _unsafe;
    public string? CommandName { get; private set; }
    public int Timeout { get; private set; } = int.MaxValue;
    public OutgoingBuffer OutgoingBuffer { get; private set; } = null!;

    internal static Command<T> GetCommand()
        => ThreadStaticPool<Command<T>>.Get();

    internal void FinalizeCommand()
    {
        if (_finalized)
        {
            OutgoingBuffer.IncLeasesConcurrently();
            return;
        }

        _finalized = true;
        OutgoingBuffer.Commit();

        if (!_unsafe)
            OutgoingBuffer.WriteArraySizePrefix(_length);
    }

    internal Command<T> Init(
        MemoryBlocks memoryBlocks,
        int blockSize,
        string command,
        bool clusterMode,
        bool @unsafe = false)
    {
        _owners = 1;
        CommandName = command;
        OutgoingBuffer = memoryBlocks.LeaseLinked(blockSize, !@unsafe, maxPrefixSize: 16);

        if (!@unsafe)
            WriteBulkUtf8(command);

        _clusterMode = clusterMode;
        _unsafe = @unsafe;

        return this;
    }

    public Command<T> WithTimeout(int timeout)
    {
        Timeout = timeout;

        return this;
    }

    public SlotsEnumerator EnumerateSlots() =>
        new(_slots.GetEnumerator());

    public Command<T> Token(string token)
    {
        return WriteBulkUtf8(token);
    }

    private Command<T> WriteBulkUtf8(string str)
    {
        _length++;

        var byteCount = Encoding.UTF8.GetByteCount(str);

        OutgoingBuffer!.WriteBulkString(
            byteCount,
            str,
            Encoding.UTF8,
            static (str, enc, span) => enc.GetBytes(str.AsSpan(), span));

        return this;
    }

    public Command<T> Key(in Key key)
    {
        _length++;

        if (_clusterMode)
        {
            _slots.Add(key.WriteAndCalculateHashSlot(OutgoingBuffer));
            return this;
        }

        key.WriteTo(OutgoingBuffer);

        return this;
    }

    public Command<T> Arg(in Value value)
    {
        _length++;

        value.WriteTo(OutgoingBuffer);

        return this;
    }

    public Command<T> Arg(in Key key)
    {
        return Key(key);
    }

    public Command<T> Arg(ReadOnlySpan<byte> arg)
    {
        _length++;

        OutgoingBuffer!.WriteBulkString(arg);

        return this;
    }

    public Command<T> Arg(int arg)
    {
        _length++;

        OutgoingBuffer!.WriteBulkString(arg);

        return this;
    }

    public Command<T> Arg(long arg)
    {
        _length++;

        OutgoingBuffer!.WriteBulkString(arg);

        return this;
    }

    public Command<T> Arg(double arg)
    {
        _length++;

        OutgoingBuffer!.WriteBulkString(arg);

        return this;
    }

    public Command<T> Arg(string key, Encoding? encoding = null)
    {
        _length++;

        encoding ??= DefaultRespEncoding.Value;

        var byteCount = encoding.GetByteCount(key);
        OutgoingBuffer!.WriteBulkString(
            byteCount,
            key,
            encoding,
            static (str, enc, span) => enc.GetBytes(str.AsSpan(), span));

        return this;
    }

    public void Dispose()
    {
        var owners = --_owners;
        if (_owners < 0)
            throw new InvalidOperationException(
                "Multiple disposals detected. This is not allowed and may corrupt the application state."
            );

        if (owners != 0)
            return;

        OutgoingBuffer.FreeByOwner();
        _length = 0;
        _slots.Clear();
        Timeout = int.MaxValue;
        _unsafe = false;
        _finalized = false;
        ThreadStaticPool<Command<T>>.Return(this);
    }

    public Command<T> WithOwnership()
    {
        _owners++;
        return this;
    }

    public readonly struct SlotsEnumerator(List<ushort>.Enumerator enumerator)
    {
        public List<ushort>.Enumerator GetEnumerator() => enumerator;
    }
}