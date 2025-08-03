namespace Respsody.Library;

public class CRC16
{
    private static readonly ushort[] Lookup = CalculateLookupTable();

    private static ushort[] CalculateLookupTable()
    {
        var crc16Table = new ushort[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (ushort)(i << 8);
            for (var j = 0; j < 8; j++)
            {
                var bitIsSet = (crc & 0x8000) != 0;
                crc <<= 1;
                if (bitIsSet)
                    crc ^= 0x1021;
            }

            crc16Table[i] = crc;
        }

        return crc16Table;
    }

    public static int Calculate(ReadOnlySpan<byte> buffer)
    {
        var crc16 = 0;
        foreach (var @byte in buffer)
            crc16 = crc16 << 8 ^ Lookup[(crc16 >> 8 ^ @byte) & 0x00FF];

        return crc16;
    }
}