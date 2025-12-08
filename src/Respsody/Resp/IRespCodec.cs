namespace Respsody.Resp;

public interface IRespCodec
{
    T Decode<T>(in OwnedRespValueVariant valueVariant);
}