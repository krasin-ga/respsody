namespace Respsody.Resp;

public enum ResponseType : byte
{
    Untyped,
    String,
    Boolean,
    Double,
    Number,
    BigNumber,
    Array,
    Map,
    Set,
    Void,
    Subscription
}