using Respsody.Resp;

namespace Respsody.Benchmarks.NonScientific;

[RespCommand("GET key", ResponseType.String)]
[RespCommand("SET key value:string", ResponseType.Void, MethodName = "SetStr")]
[RespCommand("SET key value", ResponseType.Void)]
[RespCommand("MGET key [key ...]", ResponseType.Array)]
[RespCommand("MSET key value [key value ...]", ResponseType.Void)]
[RespCommand("COMMAND DOCS [command-name:string [command-name:string ...]]", ResponseType.Map)]
[RespCommand("COMMAND DOCS [command-name:string [command-name:string ...]]", ResponseType.Array, MethodName = "CommandArray")]
public static class Commands;