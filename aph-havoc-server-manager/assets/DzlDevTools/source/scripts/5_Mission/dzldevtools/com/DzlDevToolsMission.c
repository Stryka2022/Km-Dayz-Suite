/*
	COM's `CommunityOfflineClient extends MissionGameplay` dissolved into this
	`modded class MissionGameplay`. It wires the COM ModuleManager lifecycle, hive,
	weather and the EditorMenu into the running mission.

	OFFLINE-ONLY GUARD (port rule #2): every COM behaviour here is gated behind
	IsComActive() == !GetGame().IsMultiplayer(). When the player connects to a real
	(multiplayer) server the mod is a pure no-op — module manager is never created,
	no hive/weather override, EditorMenu is never offered. GetGame().IsMultiplayer()
	is false only for the local offline mission (verified: DayZGame offline mission
	runs single-player), which is exactly where COM tooling should run.

	NOT PORTED here (port rule #2):
	  - Player spawning: COM's SpawnPlayer()/SelectPlayer is intentionally dropped.
	    The mission init.c (DzlOfflineMission) owns spawning.
	  - CommunityOfflineServer: server side stays vanilla.

	This is the ONLY file that overrides MissionGameplay.OnUpdate — the old
	dzlDevToolsInput.c poller was folded into OnUpdate() below to avoid a duplicate
	override in the same modded layer.
*/
modded class MissionGameplay
{
	protected bool m_DzlComLoaded;

	protected bool IsComActive()
	{
		// Offline dev tooling only. Real servers => no-op.
		return !GetGame().IsMultiplayer();
	}

	override void OnInit()
	{
		super.OnInit();

		if ( !IsComActive() )
			return;

		NewModuleManager();

		// Offline bootstrap (hive / weather / player spawn) belongs to the MISSION's init.c —
		// the mod must layer on top of ANY mission without re-initialising the hive (double
		// InitOffline crashes). We only bring the dev-tool module manager.

		// CameraTools/persistency resolve $saves paths against this.
		GetDayZGame().SetMissionPath( "$saves:DzlDevTools\\" );
	}

	override void OnMissionStart()
	{
		super.OnMissionStart();

		if ( !IsComActive() )
			return;

		COM_GetModuleManager().OnInit();
		COM_GetModuleManager().OnMissionStart();
	}

	override void OnMissionFinish()
	{
		if ( IsComActive() && g_com_ModuleManager )
		{
			COM_GetModuleManager().OnMissionFinish();

			CloseAllMenus();
			DestroyAllMenus();
			// Hive teardown is the mission's/engine's concern — the mod never created it.
		}

		super.OnMissionFinish();
	}

	void DzlOnMissionLoaded()
	{
		COM_GetModuleManager().OnMissionLoaded();
	}

	override void OnUpdate( float timeslice )
	{
		super.OnUpdate( timeslice );

		// --- DzlDevTools input poller (folded in from dzlDevToolsInput.c) -----
		// PluginDeveloper only exists on DIAG_DEVELOPER builds (DayZDiag); on retail
		// non-diag clients GetPlugin returns null and every action no-ops. API
		// verified against P:\scripts (uainput.c LocalPress, plugindeveloper.c).
		PluginDeveloper dev = PluginDeveloper.Cast( GetPlugin( PluginDeveloper ) );
		if ( dev )
		{
			if ( GetUApi().GetInputByName("UADzlConsole").LocalPress() )
				dev.ToggleScriptConsole();

			if ( !GetGame().GetUIManager().GetMenu() )
			{
				if ( GetUApi().GetInputByName("UADzlSpawnClipboard").LocalPress() )
					dev.SpawnFromClipboard();

				if ( GetUApi().GetInputByName("UADzlTeleport").LocalPress() )
					dev.TeleportAtCursor();

				if ( GetUApi().GetInputByName("UADzlFreeCam").LocalPress() )
					dev.ToggleFreeCamera();

				if ( GetUApi().GetInputByName("UADzlFreeCamStay").LocalPress() )
					dev.ToggleFreeCameraBackPos();
			}
		}

		// --- COM module manager tick (offline only) --------------------------
		if ( !IsComActive() )
			return;

		COM_GetModuleManager().OnUpdate( timeslice );

		if ( !m_DzlComLoaded && !GetDayZGame().IsLoading() )
		{
			m_DzlComLoaded = true;
			DzlOnMissionLoaded();
		}
	}


	override UIScriptedMenu CreateScriptedMenu(int id)
	{
		if ( IsComActive() && id == EditorMenu.MENU_ID )
		{
			return new EditorMenu();
		}

		return super.CreateScriptedMenu(id);
	}
}
