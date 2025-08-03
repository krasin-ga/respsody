namespace Respsody.Generators;

internal enum ResponseType : byte
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