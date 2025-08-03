using Respsody.Resp;

namespace Respsody.Client;

public interface IRespClient
{
    Command<T> CreateCommand<T>(string name, bool @unsafe = false)
        where T : IRespResponse;

    ValueTask<T> ExecuteCommand<T>(Command<T> command, CancellationToken cancellationToken = default)
        where T : IRespResponse;
}