using Respsody.Memory;

namespace Respsody.Network;

public readonly record struct TransmitUnit<TPayload>(OutgoingBuffer Page, TPayload? Payload);