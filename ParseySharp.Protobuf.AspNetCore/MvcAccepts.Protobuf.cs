using ParseySharp.AspNetCore;

namespace ParseySharp.AspNetCore;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AcceptsProtobufAttribute : AcceptsContentAttribute
{
  protected override Type HandlerType => typeof(ProtobufContentHandler);
}
