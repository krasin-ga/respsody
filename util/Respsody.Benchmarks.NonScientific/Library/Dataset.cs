namespace Respsody.Benchmarks.NonScientific.Library;

public readonly record struct Dataset(string DisplayName, int NumberOfKeyValuePairs, SizeInBytes SizeInBytes)
{
    public override string ToString()
    {
        return $"{DisplayName} {NumberOfKeyValuePairs}KV {SizeInBytes}";
    }
}