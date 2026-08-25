/*
	Ported from DayZCommunityOfflineMode core/ModuleManager.c.

	PORT CHANGES vs COM original:
	  - Dropped all `#include "$CurrentDir:missions\..."` lines and the
	    COM_MODULES_OLDLOADING machinery: the mod's script module compiles its
	    whole folder tree automatically, so explicit includes are neither needed
	    nor valid here.
	  - Dropped the `#ifdef MODULE_*` gates in RegisterModules(). Those defines
	    originally lived in each module's module.c (dropped above); relying on
	    cross-file #define visibility during folder compilation is fragile, so we
	    register exactly the modules we ported, unconditionally.
	  - Persistency (PersistencyModule) is intentionally NOT registered: its
	    character-menu/scene code depends on UIExtender / CustomInGameMenu which
	    do not exist in this COM copy and it owned player spawning (the mission
	    init.c owns that here). Only its self-contained Data/Load/Save helpers are
	    ported (compilable, reusable by CharacterSave/CharacterLoad).
*/

class KeyMouseActionType
{
    static int PRESS = 1;
    static int RELEASE = 2;
    static int HOLD = 4;
    static int DOUBLECLICK = 8;
    static int VALUE = 16;
}

class ModuleManager
{
    protected ref array< ref Module > m_Modules;

    void ModuleManager()
    {
        RegisterModules();
    }

    void ~ModuleManager()
    {
        m_Modules.Clear();

        delete m_Modules;
    }

    void RegisterModule( Module module )
    {
        m_Modules.Insert( module );
    }

    void RegisterModules()
    {
        m_Modules = new array< ref Module >;

        RegisterModule( new ObjectEditor );
        RegisterModule( new CameraTool );
        RegisterModule( new COMKeyBinds );
        RegisterModule( new CustomDebugMonitor );
    }

    void ReloadSettings()
    {
        for ( int i = 0; i < m_Modules.Count(); ++i)
        {
            // m_Modules.Get(i).ReloadSettings();
        }
    }

    array< ref Module > GetModules()
    {
        return m_Modules;
    }

    void OnInit()
    {
        for ( int i = 0; i < m_Modules.Count(); ++i)
        {
            m_Modules.Get(i).Init();
        }

        GetUApi().UpdateControls();
    }

    void OnMissionStart()
    {
        for ( int i = 0; i < m_Modules.Count(); ++i)
        {
            m_Modules.Get(i).onMissionStart();
        }
    }

    void OnMissionFinish()
    {
        for ( int i = 0; i < m_Modules.Count(); ++i)
        {
            m_Modules.Get(i).onMissionFinish();
        }
    }

    void OnMissionLoaded()
    {
        for ( int i = 0; i < m_Modules.Count(); ++i)
        {
            m_Modules.Get(i).onMissionLoaded();
        }
    }

    void OnUpdate( float timeslice )
    {
		bool inputIsFocused = false;

		Widget focusedWidget = GetFocus();
		if ( focusedWidget && focusedWidget.ClassName().Contains( "EditBoxWidget" ) )
		{
			inputIsFocused = true;
		}

        for ( int i = 0; i < m_Modules.Count(); ++i)
        {
            Module module = m_Modules.Get(i);

            if ( !module.IsPreventingInput() )
            {
                auto bindings = module.GetBindings();

                for ( int nBinding = 0; nBinding < bindings.Count(); ++nBinding )
                {
                    auto k_m_Binding = bindings[ nBinding ];

                    if ( !k_m_Binding.CanBeUsedInMenu() && GetGame().GetUIManager().GetMenu())
                    {
                        continue;
                    }

                    if ( inputIsFocused )
                    {
                        continue;
                    }

                    UAInput input = GetUApi().GetInputByName( k_m_Binding.GetUAInputName() );

                    int action = k_m_Binding.GetActionType();

                    if ( action & KeyMouseActionType.PRESS && input.LocalPress() )
                    {
                        GetGame().GameScript.CallFunction( GetModule( k_m_Binding.GetObject() ), k_m_Binding.GetCallBackFunction(), NULL, 0 );
                    }

                    if ( action & KeyMouseActionType.RELEASE && input.LocalRelease() )
                    {
                        GetGame().GameScript.CallFunction( GetModule( k_m_Binding.GetObject() ), k_m_Binding.GetCallBackFunction(), NULL, 0 );
                    }

                    if ( action & KeyMouseActionType.HOLD && input.LocalHold() )
                    {
                        GetGame().GameScript.CallFunction( GetModule( k_m_Binding.GetObject() ), k_m_Binding.GetCallBackFunction(), NULL, 0 );
                    }

                    if ( action & KeyMouseActionType.DOUBLECLICK && input.LocalDoubleClick() )
                    {
                        GetGame().GameScript.CallFunction( GetModule( k_m_Binding.GetObject() ), k_m_Binding.GetCallBackFunction(), NULL, 0 );
                    }

                    if ( action & KeyMouseActionType.VALUE && input.LocalValue() != 0 )
                    {
                        GetGame().GameScript.CallFunction( GetModule( k_m_Binding.GetObject() ), k_m_Binding.GetCallBackFunction(), NULL, input.LocalValue() );
                    }
                }
            }

            module.onUpdate( timeslice );
        }
    }

    Module GetModule( typename module_Type )
    {
        for ( int i = 0; i < m_Modules.Count(); ++i )
        {
            Module module = m_Modules.Get(i);

            if ( module.GetModuleType() == module_Type)
            {
                return module;
            }
        }

        return NULL;
    }

    Module GetModuleByName( string module_name )
    {
        for ( int i = 0; i < m_Modules.Count(); ++i )
        {
            Module module = m_Modules.Get( i );

            if (module.GetModuleName() == module_name)
            {
                return module;
            }
        }

        return NULL;
    }
}

ref ModuleManager g_com_ModuleManager;

ModuleManager COM_GetModuleManager()
{
    if( !g_com_ModuleManager )
    {
        g_com_ModuleManager = new ModuleManager();
    }

    return g_com_ModuleManager;
}

ModuleManager NewModuleManager()
{
    if ( g_com_ModuleManager )
    {
        delete g_com_ModuleManager;
    }

    g_com_ModuleManager = new ModuleManager();

    return g_com_ModuleManager;
}
