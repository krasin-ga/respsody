using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Respsody.Client;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using Respsody.Memory;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class ComboTests
{
    [Fact]
    public async Task FifthArgumentCommandIsCreatedAndExecutedExactlyOnce()
    {
        using var client = new RecordingClient();
        using var response = await Combo.Build(client)
            .Cmd(1, static (c, value) => c.CreateCommand<RespNumber>("INCR").Arg(value))
            .Cmd(2, static (c, value) => c.CreateCommand<RespNumber>("INCR").Arg(value))
            .Cmd(3, static (c, value) => c.CreateCommand<RespNumber>("INCR").Arg(value))
            .Cmd(4, static (c, value) => c.CreateCommand<RespNumber>("INCR").Arg(value))
            .Cmd(5, static (c, value) => c.CreateCommand<RespNumber>("INCR").Arg(value))
            .Execute();

        Assert.Equal(5, client.Created);
        Assert.Equal(5, client.Executed);
        Assert.Equal(1, client.Batches);
        Assert.Equal(1, response.Value5.ToInt64());
    }

    private sealed class RecordingClient : IDirectRespClient, IDisposable
    {
        private readonly RespClient _client = new(new RespClientOptions(), new DefaultConnectionProcedure(new ConnectionOptions()));
        private Combo? _combo;
        public int Created { get; private set; }
        public int Executed { get; private set; }
        public int Batches { get; private set; }
        public IReadOnlyDictionary<string, string?> ConnectionConfig => _client.ConnectionConfig;

        public Command<T> CreateCommand<T>(string name, bool @unsafe = false) where T : IRespResponse
        {
            Created++;
            return _client.CreateCommand<T>(name, @unsafe);
        }

        public ValueTask<T> ExecuteCommand<T>(Command<T> command, CancellationToken cancellationToken = default)
            where T : IRespResponse => throw new NotSupportedException();

        public ComboCommand<T> Pack<T>(Combo combo, Command<T> command, CancellationToken token = default) where T : IRespResponse
        {
            _combo = combo;
            return _client.Pack(combo, command, token);
        }

        public void Execute(Combo combo)
        {
            Batches++;
            foreach (var unit in combo.PreparedPayload)
            {
                Executed++;
                unit.Payload.Complete(new Frame<RespContext>(new RespContext { Type = RespType.Number },
                    ":1\r\n"u8.ToArray(), new Lifetime()), Environment.TickCount, null);
                unit.Page.FreeByDeliveryPipeline();
            }
        }

        public void Dispose()
        {
            _combo?.Dispose();
            _client.Dispose();
        }
    }

    private sealed class Lifetime : IDisposable
    {
        public void Dispose() { }
    }
}
