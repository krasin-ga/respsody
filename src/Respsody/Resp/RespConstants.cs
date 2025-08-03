namespace Respsody.Resp;

public static class RespConstants
{
    public static class Double
    {
        public static readonly byte[] PositiveInfinity = "+inf"u8.ToArray();
        public static readonly byte[] NegativeInfinity = "-inf"u8.ToArray();
        public static readonly byte[] NaN = "nan"u8.ToArray();
    }
}