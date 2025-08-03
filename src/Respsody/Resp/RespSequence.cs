namespace Respsody.Resp;

public sealed class RespSequence(RespSequencesPool pool) : IDisposable
{
    public RespType Type { get; private set; }
    public int StartIndex { get; private set; }
    public int Length { get; private set; }

    public RespSequence? Next { get; private set; }

    private void Reset()
    {
        Type = default;
        StartIndex = 0;
        Length = 0;
        Next = null;

        pool.Return(this);
    }

    public void Dispose()
    {
        var current = this;

        while (current != null)
        {
            var tmp = current;
            current = Next;

            tmp.Reset();
        }
    }
}