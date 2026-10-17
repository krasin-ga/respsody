using Respsody.Client;
using Respsody.Client.Connection;
using Respsody.Client.Connection.Options;
using Respsody.Client.Options;
using Respsody.Memory;
using Respsody.Resp;
using Xunit;

namespace Respsody.Tests;

public class CommandOwnershipTests
{
    [Fact]
    public void DisposingUnsentCommandReturnsItsOutgoingBuffer()
    {
        var blocks = new MemoryBlocks();
        using var client = new RespClient(new RespClientOptions { MemoryBlocks = blocks },
            new DefaultConnectionProcedure(new ConnectionOptions()));
        var command = client.CreateCommand<RespVoid>("PING");
        var outgoing = command.OutgoingBuffer;

        command.Dispose();

        var reused = blocks.LeaseLinked(128, true, 16);
        try
        {
            Assert.Same(outgoing, reused);
        }
        finally
        {
            reused.Commit();
            reused.FreeByDeliveryPipeline();
            reused.FreeByOwner();
        }
    }
}
