# ParseySharp.Config

Parse IConfiguration with ParseySharp parsers and bind results to Microsoft.Extensions.Options. No nulls or default values ever again.

## Install

- NuGet packages to install:
  - ParseySharp.Config
  - Microsoft.Extensions.Options
  - Microsoft.Extensions.Configuration.Abstractions

Commands:

```bash
dotnet add package ParseySharp.Config
dotnet add package Microsoft.Extensions.Options
dotnet add package Microsoft.Extensions.Configuration.Abstractions
```

## appsettings.json

```json
{
  "mySettings": {
    "enabled": true,
    "endpoint": "https://api.example.com",
    "retries": 3
  }
}
```

## Define a model and parser

```csharp
using ParseySharp;

public sealed record MySettings(bool Enabled, string Endpoint, int Retries);

public static class MyParsers
{
  public static readonly Parse<MySettings> Settings = (
    Parse.BoolFlex().At("enabled"),
    Parse.As<string>().At("endpoint"),
    Parse.Int32Flex().At("retries")
  ).Apply((enabled, endpoint, retries) =>
    new MySettings(enabled, endpoint, retries));
}
```

## Wire up in Program.cs

```csharp
using Microsoft.Extensions.Options;
using ParseySharp; // Parse<>

var builder = WebApplication.CreateBuilder(args);

builder.Services
  .AddOptions<MySettings>()
  .ParseWith(
    builder.Configuration,
    MyParsers.Settings,
    sectionName: "mySettings");

var app = builder.Build();
```

## Access the parsed options

```csharp
// Minimal API endpoint
app.MapGet("/settings", (IOptions<MySettings> opts) => Results.Ok(opts.Value));

// Or resolve from the container
var settings = app.Services.GetRequiredService<IOptions<MySettings>>().Value;
```

## Failing appsettings.json

```json
{
  "mySettings": {
    "enabled": true,
    "endpoint": "https://api.example.com",
    "retries": "four"
  }
}
```

Output at startup:

```text
Failed to bind ParseySharp.SampleWeb.MySettings from configuration mySettings:

ParsePathErr { Message = Null or missing value, Expected = String, Actual = Fail(()), Path = [endpoint] }
ParsePathErr { Message = Invalid integer: four, Expected = Object, Actual = Some(four), Path = [retries] }
```

## Notes

- Use `ParseWith(configuration, parser, "sectionName")` to parse a section; omit `sectionName` to parse the root.
- Works with any IConfiguration provider (JSON, environment variables, etc.).
