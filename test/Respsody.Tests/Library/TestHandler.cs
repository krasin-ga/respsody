using System;
using System.Threading.Tasks;
using Respsody.Client;
using Xunit.Abstractions;

namespace Respsody.Tests.Library;

public class TestHandler(
    ITestOutputHelper? output = null,
    Action<IRespClient, Exception>? onPanic = null,
    Func<IRespClient, int, ValueTask>? onConnected = null,
    Action<IRespClient, Exception?, int>? onDisconnected = null,
    Action<IRespClient, Exception>? onConnectionError = null,
    Action<int>? onMemoryBlockCreated = null,
    Action<int>? onMemoryBlockDestructed = null,
    Action<int>? onMemoryBlockResurrected = null,
    Action<IRespClient, int, string>? onCommandExecuted = null,
    Action<IRespClient, int, string>? onCommandFailed = null,
    Action<IRespClient, int, string>? onCommandTimedOut = null,
    Action<IRespClient, int, string>? onCommandCancelled = null
) : IRespClientHandler
{
    private void Log(string message)
    {
        try
        {
            output?.WriteLine(message);
        }
        catch
        {
            //
        }
    }

    public void OnPanic(IRespClient client, Exception exception)
    {
        Log($"[PANIC]       {exception}");
        onPanic?.Invoke(client, exception);
    }

    public ValueTask OnConnected(IRespClient client, int generation)
    {
        Log($"[Connected]   gen={generation}");
        return onConnected?.Invoke(client, generation) ?? ValueTask.CompletedTask;
    }

    public void OnDisconnected(IRespClient client, Exception? exception, int generation)
    {
        Log($"[Disconnected] gen={generation}, ex={exception}");
        onDisconnected?.Invoke(client, exception, generation);
    }

    public void OnConnectionError(IRespClient client, Exception exception)
    {
        Log($"[ConnError]   {exception}");
        onConnectionError?.Invoke(client, exception);
    }

    public void OnMemoryBlockCreated(int blockSize)
    {
        Log($"[MemCreated]  {blockSize} bytes");
        onMemoryBlockCreated?.Invoke(blockSize);
    }

    public void OnMemoryBlockDestructed(int blockSize)
    {
        Log($"[MemDestruct] {blockSize} bytes");
        onMemoryBlockDestructed?.Invoke(blockSize);
    }

    public void OnMemoryBlockResurrected(int blockSize)
    {
        Log($"[MemResurrect]{blockSize} bytes");
        onMemoryBlockResurrected?.Invoke(blockSize);
    }

    public void OnCommandExecuted(IRespClient client, int elapsedTimeInMs, string command)
    {
        Log($"[CmdExecuted] {command} ({elapsedTimeInMs} ms)");
        onCommandExecuted?.Invoke(client, elapsedTimeInMs, command);
    }

    public void OnCommandFailed(IRespClient client, int elapsedTimeInMs, string command)
    {
        Log($"[CmdFailed]   {command} ({elapsedTimeInMs} ms)");
        onCommandFailed?.Invoke(client, elapsedTimeInMs, command);
    }

    public void OnCommandTimedOut(IRespClient client, int elapsedTimeInMs, string command)
    {
        Log($"[CmdTimeout]  {command} ({elapsedTimeInMs} ms)");
        onCommandTimedOut?.Invoke(client, elapsedTimeInMs, command);
    }

    public void OnCommandCancelled(IRespClient client, int elapsedTimeInMs, string command)
    {
        Log($"[CmdCancel]   {command} ({elapsedTimeInMs} ms)");
        onCommandCancelled?.Invoke(client, elapsedTimeInMs, command);
    }
}