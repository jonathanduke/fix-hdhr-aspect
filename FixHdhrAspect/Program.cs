// Template: https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service?pivots=dotnet-7-0#rewrite-the-program-class

using JonathanDuke.FixHdhrAspect;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "HDHomeRun Proxy Service";
});

// EventLog is only supported on Windows: https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1416#how-to-fix-violations
if (OperatingSystem.IsWindows())
{
    LoggerProviderOptions.RegisterProviderOptions<
        EventLogSettings, EventLogLoggerProvider>(builder.Services);
}

builder.Services.AddSingleton<ProxyService>();
builder.Services.AddHostedService<Worker>();

// See: https://github.com/dotnet/runtime/issues/47303
builder.Logging.AddConfiguration(
    builder.Configuration.GetSection("Logging"));

IHost host = builder.Build();
host.Run();
