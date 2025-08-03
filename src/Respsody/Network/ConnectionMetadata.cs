using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;

namespace Respsody.Network;

public class ConnectionMetadata(EndPoint endPoint, IReadOnlyDictionary<string, object?> metadata)
{
    public EndPoint EndPoint { get; } = endPoint;
    public object? this[string s] => metadata.GetValueOrDefault(s);

    public bool TryGetKeyOfType<T>(string key, [NotNullWhen(true)] out T? value)
    {
        if (metadata.TryGetValue(key, out var obj) && obj is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }

    public T CastOrThrow<T>(string key, Func<object?, T> castFunc)
    {
        if (!metadata.TryGetValue(key, out var value))
            throw new KeyNotFoundException(nameof(key));
        try
        {
            return castFunc(value);
        }
        catch (Exception exception)
        {
            throw new InvalidCastException($"Cannot cast metadata key `{key}` to {typeof(T).Name}", exception);
        }
    }

    public string ToDebugString()
    {
        return $"{EndPoint} {JsonSerializer.Serialize(metadata)}";
    }
}