using System.Buffers;

namespace Respsody.Memory;

public delegate void WriteToBuffer<in T>(T value, IBufferWriter<byte> destination);
public delegate int WriteToSpan<in T>(T value, Span<byte> destination);
public delegate int WriteToSpan<in T, in TArgument>(T value, TArgument argument, Span<byte> destination);