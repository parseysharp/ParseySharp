namespace ParseySharp;

public sealed record NotApplicable<S>(S Source);
public sealed record Value<T>(T Get);
public sealed record Null;
public sealed record Absent;
