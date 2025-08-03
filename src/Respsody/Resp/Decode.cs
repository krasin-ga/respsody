using Respsody.Memory;

namespace Respsody.Resp;

public delegate T Decode<out T>(in RespValueVariant valueVariant);
public delegate T DecodeSlice<out T>(in Frame<RespContext> frame);