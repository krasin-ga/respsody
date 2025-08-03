namespace Respsody.Library;

internal static class CastingExtensions
{
    public static T Cast<T>(this object? input)
    {
        if (input is null)
            throw new NullReferenceException();

        return (T)input;
    }
}