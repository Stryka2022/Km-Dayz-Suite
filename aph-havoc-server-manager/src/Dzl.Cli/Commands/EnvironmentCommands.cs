using System.CommandLine;
using System.Text.Json;
using Dzl.Core.Config;
using Dzl.Core.Env;

namespace Dzl.Cli.Commands;

/// <summary>Cross-platform host and .NET runtime discovery.</summary>
internal static class EnvironmentCommands
{
    public static Command Environment()
    {
        var json = new Option<bool>("--json", "Print machine-readable JSON.");
        var command = new Command("environment",
            "Show the operating system, CPU architecture and installed .NET SDK/runtime versions.")
        {
            json
        };
        command.AddAlias("env");
        command.SetHandler(ctx =>
        {
            var report = DotNetEnvironmentDetector.Detect();
            if (ctx.ParseResult.GetValueForOption(json))
            {
                Console.WriteLine(JsonSerializer.Serialize(report, ConfigStore.Json));
                return;
            }

            Console.WriteLine($"platform:       {report.Platform}");
            Console.WriteLine($"architecture:   {report.Architecture}");
            Console.WriteLine($"dotnet host:    {(report.HostVersion.Length > 0 ? report.HostVersion : "(unavailable)")}");
            Console.WriteLine($".NET 11:        {(report.HasMajor11 ? "detected" : "not installed")}");
            Console.WriteLine($"SDKs:           {(report.Sdks.Count > 0 ? string.Join(", ", report.Sdks) : "(none found)")}");
            Console.WriteLine($"runtimes:       {(report.Runtimes.Count > 0 ? string.Join(", ", report.Runtimes) : "(none found)")}");
        });
        return command;
    }
}
