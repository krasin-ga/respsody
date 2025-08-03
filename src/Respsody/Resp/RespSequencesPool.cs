namespace Respsody.Resp;

public class RespSequencesPool
{
    public RespSequence Lease()
    {
        return new RespSequence(this);
    }

    public void Return(RespSequence respSequence)
    {
    }
}