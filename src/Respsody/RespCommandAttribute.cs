using Respsody.Resp;

namespace Respsody;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class RespCommandAttribute : Attribute
{
    public string? MethodName { get; set; }
    public string? Namespace { get; set; }
    public VisibilityType Visibility { get; set; }

    public RespCommandAttribute(string commandDefinition, ResponseType responseType)
    {
    }

    public enum VisibilityType
    {
        Public,
        Internal
    }
}