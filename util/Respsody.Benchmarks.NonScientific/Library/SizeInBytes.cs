using System.Globalization;

namespace Respsody.Benchmarks.NonScientific.Library;

public readonly struct SizeInBytes(long bytes)
{
    public override string ToString()
    {
        return bytes switch
        {
            >= 1024L * 1024 * 1024 => $"{(bytes / (1024.0 * 1024 * 1024)).ToString("F2", CultureInfo.InvariantCulture)} GiB",
            >= 1024L * 1024 => $"{(bytes / (1024.0 * 1024)).ToString("F2", CultureInfo.InvariantCulture)} MiB",
            >= 1024L => $"{(bytes / 1024.0).ToString("F2", CultureInfo.InvariantCulture)} KiB",
            _ => $"{bytes} B"
        };
    }
}