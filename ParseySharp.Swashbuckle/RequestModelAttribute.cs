namespace ParseySharp.Swashbuckle;

[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequestModelAttribute : System.Attribute
{
  public System.Type ModelType { get; }
  public RequestModelAttribute(System.Type modelType) => ModelType = modelType;
}

[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequestModelAttribute<TModel> : System.Attribute
{
  public System.Type ModelType { get; } = typeof(TModel);
}
