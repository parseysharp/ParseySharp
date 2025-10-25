namespace ParseySharp;

public static class PsDebug
{
  public static A PsDbg<A>(this A a, string msg) {
    Console.WriteLine($"{msg}: {a}");
    return a;
  }
}
