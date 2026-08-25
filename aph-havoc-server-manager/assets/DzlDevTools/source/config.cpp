class CfgPatches
{
    class DzlDevTools
    {
        units[] = {};
        weapons[] = {};
        requiredVersion = 0.1;
        requiredAddons[] = { "DZ_Data" };
        author = "Borek";
    };
};

class CfgMods
{
    class DzlDevTools
    {
        dir = "DzlDevTools";
        name = "DzlDevTools";
        author = "Borek";
        type = "mod";
        dependencies[] = { "Game", "World", "Mission" };
        inputs = "DzlDevTools/inputs.xml";
        class defs
        {
            // modded DayZPlayerCamera1stPerson must compile in the same module as the vanilla class (4_World)
            class worldScriptModule   { value = ""; files[] = { "DzlDevTools/scripts/4_World" }; };
            class missionScriptModule { value = ""; files[] = { "DzlDevTools/scripts/5_Mission" }; };
        };
    };
};
