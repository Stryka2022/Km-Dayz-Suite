using Dzl.Core.Economy;
using FluentAssertions;

public class OfflineInitTests
{
    private const string Vanilla =
@"void main() {}

class CustomMission: MissionServer
{
}

Mission CreateCustomMission(string path)
{
	return new CustomMission();
}
";

    [Fact]
    public void Transform_wraps_the_original_and_injects_the_offline_mission()
    {
        var r = OfflineInit.Transform(Vanilla);

        r.Should().NotBeNull();
        r!.Should().Contain("Mission CreateCustomMission_dzlOrig(string path)")   // original renamed
            .And.Contain("class DzlOfflineMission : MissionGameplay")             // client mission injected
            .And.Contain("CreateCustomMission_dzlOrig(path)");                    // server path delegates to it
        // The original mission class body is preserved verbatim.
        r.Should().Contain("class CustomMission: MissionServer");
    }

    [Fact]
    public void Transform_is_idempotent_returns_null_when_already_patched()
    {
        var once = OfflineInit.Transform(Vanilla);
        OfflineInit.Transform(once!).Should().BeNull();
    }

    [Fact]
    public void Transform_returns_null_when_there_is_no_CreateCustomMission()
        => OfflineInit.Transform("void main() {}\n").Should().BeNull();

    [Fact]
    public void Transform_wraps_a_non_vanilla_CreateCustomMission_too()
    {
        // COM-style branched mission — the wrap must still work (any mission/map).
        var com = "Mission CreateCustomMission(string path)\n{\n\tif (x) return new A();\n\treturn new B();\n}\n";
        var r = OfflineInit.Transform(com);
        r!.Should().Contain("Mission CreateCustomMission_dzlOrig(string path)")
            .And.Contain("return new B();");   // original body intact
    }
}
