using Dzl.Core.Projects;
using FluentAssertions;

namespace Dzl.Core.Tests;

public class ProjectPathsInstanceTests
{
    [Theory]
    [InlineData("My Chernarus PvE", "My_Chernarus_PvE")]
    [InlineData("  24/7 | UK #1  ", "Server_24_7_UK_1")]
    [InlineData("***", "Server")]
    [InlineData("already_safe", "already_safe")]
    public void SafeInstanceName_creates_a_valid_key(string input, string expected)
    {
        var actual = ProjectPaths.SafeInstanceName(input);
        actual.Should().Be(expected);
        ProjectPaths.IsValidName(actual).Should().BeTrue();
    }
}
