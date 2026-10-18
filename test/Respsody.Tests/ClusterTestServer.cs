using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Respsody.Network;

namespace Respsody.Tests;

internal sealed class ClusterTestServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _closed = new();
    private readonly ConcurrentDictionary<int, Task> _connections = new();
    private readonly Task _accept;
    private int _connectionCount;

    public ClusterTestServer()
    {
        _listener.Start();
        Endpoint = (IPEndPoint)_listener.LocalEndpoint;
        _accept = AcceptConnections();
    }

    public IPEndPoint Endpoint { get; }
    public string Address => Endpoint.ToString();
    public string Topology => $"node0 {Address}@17000 myself,master - 0 0 1 connected 0-16383\n";
    public Func<string[], int, Task<string>>? Respond { get; set; }
    public int ConnectionCount => Volatile.Read(ref _connectionCount);
    public string Mode { get; set; } = "cluster";

    public Task ConnectionClosed(int index) => _closed[index].Task.WaitAsync(TimeSpan.FromSeconds(10));
    public bool IsClosed(int index) => _closed[index].Task.IsCompleted;
    public IConnectionProcedure CreateConnectionProcedure() => new ConnectionProcedure(this);
    public static string Bulk(string value) => $"${Encoding.UTF8.GetByteCount(value)}\r\n{value}\r\n";

    private async Task AcceptConnections()
    {
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                var socket = await _listener.AcceptSocketAsync(_cancellation.Token);
                var index = Interlocked.Increment(ref _connectionCount);
                _closed[index] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _connections[index] = HandleConnection(socket, index);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
    }

    private async Task HandleConnection(Socket socket, int index)
    {
        using var stream = new NetworkStream(socket, ownsSocket: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        try
        {
            while (await reader.ReadLineAsync(_cancellation.Token) is { } line)
            {
                if (!line.StartsWith('*'))
                    throw new InvalidDataException($"Expected command array, received {line}");
                var arguments = new string[int.Parse(line.AsSpan(1))];
                for (var i = 0; i < arguments.Length; i++)
                {
                    var lengthLine = await reader.ReadLineAsync(_cancellation.Token) ?? throw new EndOfStreamException();
                    var length = int.Parse(lengthLine.AsSpan(1));
                    var argument = new char[length];
                    await reader.ReadBlockAsync(argument.AsMemory(), _cancellation.Token);
                    arguments[i] = new string(argument);
                    if (await reader.ReadLineAsync(_cancellation.Token) != "")
                        throw new InvalidDataException("Missing command terminator");
                }

                var response = Respond is { } respond
                    ? await respond(arguments, index)
                    : Bulk(Topology);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(response), _cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (IOException) { }
        finally
        {
            _closed[index].TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync();
        _listener.Stop();
        await _accept;
        await Task.WhenAll(_connections.Values).WaitAsync(TimeSpan.FromSeconds(10));
        _cancellation.Dispose();
    }

    private sealed class ConnectionProcedure(ClusterTestServer server) : IConnectionProcedure
    {
        public IReadOnlyDictionary<string, string?> Config { get; } = new Dictionary<string, string?>();
        public TimeSpan Timeout => TimeSpan.FromSeconds(5);

        public async Task<ConnectedSocket> Connect(CancellationToken token)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(server.Endpoint, token);
                return new ConnectedSocket(socket, new ConnectionMetadata(server.Endpoint, new Dictionary<string, object?>
                {
                    ["role"] = "master",
                    ["mode"] = server.Mode
                }));
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }
}
