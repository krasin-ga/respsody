using Respsody.Resp;

namespace Respsody.Benchmarks.Library;

[RespCommand("GET key", ResponseType.String)]
[RespCommand("GET key", ResponseType.Untyped, MethodName = "GetUntyped")]
[RespCommand("SET key value", ResponseType.Void)]
[RespCommand("MGET key [key ...]", ResponseType.Array)]
[RespCommand("MSET key value [key value ...]", ResponseType.Void)]
public static class Commands;