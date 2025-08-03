using Respsody.Client;

namespace Respsody.Benchmarks.NonScientific.Library;

public class RespClientHandler : IRespClientHandler
{
    private int _totalMemory;

    public void OnPanic(IRespClient client, Exception exception)
    {
    }

    public ValueTask OnConnected(IRespClient client, int generation)
    {
        return ValueTask.CompletedTask;
    }

    public void OnDisconnected(IRespClient client, Exception? exception, int generation)
    {
    }

    public void OnConnectionError(IRespClient client, Exception exception)
    {
    }

    public void OnMemoryBlockCreated(int blockSize)
    {
        Console.WriteLine($"Threads={ThreadPool.ThreadCount} | {Environment.CurrentManagedThreadId}  | CREATED BLOCK {blockSize} | TOTAL {new SizeInBytes(Interlocked.Add(ref _totalMemory, blockSize))}");
    }

    public void OnMemoryBlockDestructed(int blockSize)
    {
        Console.WriteLine("DESTRUCTED");
    }

    public void OnMemoryBlockResurrected(int blockSize)
    {

        Console.WriteLine($"Threads={ThreadPool.ThreadCount} | {Environment.CurrentManagedThreadId} | RESURRECTED {blockSize}");
    }

    public void OnCommandExecuted(IRespClient client, int elapsedTimeInMs, string command)
    {
    }

    public void OnCommandFailed(IRespClient client, int elapsedTimeInMs, string command)
    {
    }

    public void OnCommandTimedOut(IRespClient client, int elapsedTimeInMs, string command)
    {
    }

    public void OnCommandCancelled(IRespClient client, int elapsedTimeInMs, string command)
    {
    }
}