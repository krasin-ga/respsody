namespace Respsody.Network;

public interface IPayload
{
    bool OnAboutToWrite(int socketId, int ticks);
    bool TryExpire(int ticks);
    void LinkedCancel(int ticks);
}