using Dzl.Core.Config;
using Dzl.Core.Launch;
using FluentAssertions;

public class ProcessManagerTests
{
    [Fact]
    public void ParseImage_extracts_first_csv_field()
        => ProcessManager.ParseImage("\"DayZDiag_x64.exe\",\"4242\",\"Console\",\"1\",\"123,456 K\"")
            .Should().Be("DayZDiag_x64.exe");

    [Fact]
    public void ParseImage_returns_null_for_info_line()
        => ProcessManager.ParseImage("INFO: No tasks are running which match the specified criteria.")
            .Should().BeNull();

    [Fact]
    public void ParseImage_returns_null_for_empty()
        => ProcessManager.ParseImage("").Should().BeNull();

    [Fact]
    public void ServerInstallPath_falls_back_to_dayz_path_when_blank()
        => ProcessManager.ServerInstallPath(DzlConfig.Default() with { DayzPath = @"E:\DayZ", DayzServerPath = "" })
            .Should().Be(@"E:\DayZ");

    [Fact]
    public void ServerInstallPath_uses_dayz_server_path_when_set()
        => ProcessManager.ServerInstallPath(DzlConfig.Default() with { DayzPath = @"E:\DayZ", DayzServerPath = @"E:\DayZServer" })
            .Should().Be(@"E:\DayZServer");
}
