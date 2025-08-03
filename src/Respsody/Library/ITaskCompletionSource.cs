namespace Respsody.Library;

internal interface ITaskCompletionSource
{
    void SetException(Exception exception);
    void SetResult<TResult>(TResult result);
}