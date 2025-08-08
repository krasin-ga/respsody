using Respsody.Resp;

namespace Respsody.Tests;

[RespCommand(
    "ZADD key [NX | XX] [GT | LT] [CH] [INCR] score:float member:string [score:float member:string ...]",
    ResponseType.String,
    MethodName = "SortedAdd")]
[RespCommand("COMMAND DOCS [command-name:string [command-name:string ...]]", ResponseType.Map)]
[RespCommand("MGET key [key ...]", ResponseType.Array)]
[RespCommand("GET key", ResponseType.String)]
[RespCommand("SET key value", ResponseType.Void)]
[RespCommand("CLUSTER INFO", ResponseType.String)]
[RespCommand("CLUSTER NODES", ResponseType.String)]
[RespCommand("ZINTER numkeys:int key [key ...] [WEIGHTS weight:int [weight:int ...]] [AGGREGATE <SUM | MIN | MAX>] [WITHSCORES]", ResponseType.Array)]
[RespCommand("SUBSCRIBE channel:key [channel:key ...]", ResponseType.Subscription)]
[RespCommand("PUBLISH channel:key message:value", ResponseType.Number)]
[RespCommand("PUBLISH channel:key message:int", ResponseType.Number)]
[RespCommand("WATCH key [key ...]", ResponseType.Void)]
[RespCommand("MULTI", ResponseType.Void)]
[RespCommand("EXEC", ResponseType.Array)]
[RespCommand("INCR key", ResponseType.Number)]
[RespCommand("INCR key", ResponseType.Number)]
[RespCommand("EVAL script:string numkeys:int [key [key ...]] [arg:value [arg:value ...]]", ResponseType.Untyped)]
[RespCommand("EVAL INCR=`return redis.call('incrby', KEYS[1], ARGV[1])` INCR_NUM_KEYS=`1` key arg:int", ResponseType.Number, MethodName = "EvalIncrBy")]
public static class Commands;