using Respsody.Resp;

namespace Respsody.Client;

/// <summary>
/// Represents a client that is connected to a specific node.
/// </summary>
public interface IDirectRespClient : IRespClient
{
    IReadOnlyDictionary<string, string?> ConnectionConfig { get; }

    ComboCommand<T> Pack<T>(Combo combo, Command<T> command, CancellationToken token = default)
        where T : IRespResponse;

    void Execute(Combo combo);
}