namespace Respsody.Library;

internal static class Constants
{
    public const int MaxSizeOnStack = 256;
    public const int StreamedLength = -100;

    public const byte CR = (byte)'\r';
    public const byte LF = (byte)'\n';

    public static readonly byte[] CRLF = [CR, LF];
}