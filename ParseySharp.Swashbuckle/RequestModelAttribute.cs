namespace ParseySharp.Swashbuckle;

[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequestModelAttribute(System.Type modelType) : System.Attribute
{
  public System.Type ModelType { get; } = modelType;
}

[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequestModelAttribute<TModel> : System.Attribute
{
  public System.Type ModelType { get; } = typeof(TModel);
}
