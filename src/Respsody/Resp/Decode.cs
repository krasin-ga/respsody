namespace Respsody.Resp;

public delegate T Decode<out T>(in OwnedRespValueVariant variant);
public delegate T DecodeFrame<out T>(in OwnedRespFrame frame);