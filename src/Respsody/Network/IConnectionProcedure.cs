namespace Respsody.Network;

public interface IConnectionProcedure
{
    IReadOnlyDictionary<string, string?> Config { get; }
    TimeSpan Timeout { get; }
    Task<ConnectedSocket> Connect(CancellationToken token);
}