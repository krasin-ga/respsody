namespace Respsody.Memory;

public delegate int WriteToSpan<in T>(T value, Span<byte> destination);
public delegate int WriteToSpan<in T, in TArgument>(T value, TArgument argument, Span<byte> destination);
