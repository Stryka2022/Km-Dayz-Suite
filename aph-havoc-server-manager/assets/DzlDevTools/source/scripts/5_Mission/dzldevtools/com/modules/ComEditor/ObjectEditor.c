/*
	Ported from DayZCommunityOfflineMode core/modules/ComEditor/ObjectEditor.c (unchanged).
	SceneData is a vanilla 4_World type (P:\scripts\4_world\classes\sceneeditor\scenedata.c);
	5_Mission code may reference it. Persistence paths use $saves: per port rules.
*/
class ObjectEditor extends Module
{
	protected bool m_ObjectEditorActive = false;
	protected bool m_IsDragging;
	Object m_SelectedObject;
	Object building;

	autoptr array<Object> m_Objects = new array<Object>;

	string BASE_COM_DIR = "$saves:DzlDevTools";
	string BASE_SCENE_DIR = BASE_COM_DIR + "\\Scenes";

	ref SceneManager sceneManager;
	SceneData currentSceneData;

	void ObjectEditor()
	{
		sceneManager = new SceneManager();
	}

	void ExportObjectLoad() // requested by robotstar78
	{
		// Copy paste your exported objects in here to make it look like the example below:
		/*
		SpawnObject( "Land_CementWorks_Hall2_Brick", "12941.188477 60.166824 5238.811523", "0.000000 0.000000 0.000000" );
		*/
	}

	void SpawnObject( string type, vector position, vector orientation )
	{
	    auto obj = GetGame().CreateObject( type, position );
	    obj.SetPosition( position );
	    obj.SetOrientation( orientation );
	    obj.SetOrientation( obj.GetOrientation() );
	    obj.Update();
	    obj.SetAffectPathgraph( true, false );
        GetGame().GetCallQueue( CALL_CATEGORY_SYSTEM ).CallLater( GetGame().UpdatePathgraphRegionByObject, 100, false, obj );

	    m_Objects.Insert( obj );
	}

	void addObject( Object trackedObject )
	{
		m_Objects.Insert( trackedObject );
	}

	override void Init()
	{
		super.Init();

		LoadScene();
		ExportObjectLoad();
	}

	override void RegisterKeyMouseBindings()
	{
		KeyMouseBinding objectSelect  = new KeyMouseBinding( GetModuleType(), "ClickObject"  , "Selects object on cursor.", true  );
		KeyMouseBinding objectDrag    = new KeyMouseBinding( GetModuleType(), "DragObject"   , "Drag objects on cursor.",   true  );
		KeyMouseBinding objectDelete  = new KeyMouseBinding( GetModuleType(), "DeleteObject" , "Deletes selected object.",  true  );
		KeyMouseBinding objectGround  = new KeyMouseBinding( GetModuleType(), "GroundObject" , "Snaps objects to ground.",  true  );
		KeyMouseBinding sceneSave     = new KeyMouseBinding( GetModuleType(), "ExportScene"  , "Saves current scene of objects", true);
		KeyMouseBinding tabFix        = new KeyMouseBinding( GetModuleType(), "TabFix"       , "Fixes issue with tabbing out of the game", true );

		objectDelete.AddBinding( "kDelete" );
		sceneSave.AddBinding( "kLControl" );
		sceneSave.AddBinding( "kS" );
		tabFix.AddBinding( "kLMenu" );

		objectSelect.AddBinding( "mBLeft" ); // Left Click
		objectDrag.AddBinding( "mBLeft" );
		objectDrag.SetActionType( KeyMouseActionType.HOLD );
		objectGround.AddBinding( "mBMiddle" );

		RegisterKeyMouseBinding( objectSelect );
		RegisterKeyMouseBinding( objectDrag   );
		RegisterKeyMouseBinding( objectDelete );
		RegisterKeyMouseBinding( objectGround );
		RegisterKeyMouseBinding( sceneSave );
		RegisterKeyMouseBinding( tabFix );
	}

    void TabFix()
    {
        m_SelectedObject = NULL;
    }

	void ExportScene()
	{
		string toCopy;
        toCopy += "//Spawn helper function\n";
        toCopy += "void SpawnObject( string type, vector position, vector orientation )\n";
        toCopy += "{\n";
        toCopy += "    auto obj = GetGame().CreateObject_WIP( type, position, ECE_CREATEPHYSICS );\n";
        toCopy += "    obj.SetFlags( EntityFlags.STATIC, false );\n";
        toCopy += "    obj.SetPosition( position );\n";
        toCopy += "    obj.SetOrientation( orientation );\n";
        toCopy += "    obj.SetOrientation( obj.GetOrientation() ); //Collision fix\n";
        toCopy += "    obj.Update();\n";
        toCopy += "    obj.SetAffectPathgraph( true, false );\n";
        toCopy += "    if( obj.CanAffectPathgraph() ) GetGame().GetCallQueue( CALL_CATEGORY_SYSTEM ).CallLater( GetGame().UpdatePathgraphRegionByObject, 100, false, obj );\n";
        toCopy += "}\n";
        toCopy += "\n";
        toCopy += "//Your custom spawned objects\n";

		foreach( Object m_object : m_Objects )
		{
			toCopy = toCopy + "SpawnObject( \"" + m_object.GetType() + "\", \"" + COM_VectorToString( m_object.GetPosition() ) + "\", \"" + COM_VectorToString( m_object.GetOrientation() ) + "\" );\n";
		}

		GetGame().CopyToClipboard( toCopy );

		COM_Message( "Copied to clipboard" );
	}

	void SaveScene()
	{
		auto exportFile = OpenFile( "$saves:COMObjectEditorSave.json", FileMode.WRITE );

        if( !exportFile )
        {
            COM_Message( "Error writing COMObjectEditorSave.json. Current changes could not NOT be saved!!!" );
            return;
        }

        FPrint( exportFile, "{\"name\":\"latest\",\"m_SceneObjects\":[" );

        auto serial = new JsonSerializer;
        string file_content;
        foreach(int nObject, Object object : m_Objects )
        {
            auto objectParam = new Param3<string, vector, vector>( object.GetType(), object.GetPosition(), object.GetOrientation() );
            serial.WriteToString( objectParam, false, file_content );
            FPrint( exportFile, file_content );
            if( nObject < m_Objects.Count() - 1 ) FPrint( exportFile, "," );
        }

        FPrint( exportFile, "]}" );

        CloseFile( exportFile );

		COM_Message( "Saved objects to COMObjectEditorSave.json (User/Documents/DayZ)." );
	}

	void LoadScene()
	{
		SceneSaveST scene = new SceneSaveST();

		JsonFileLoader<SceneSaveST>.JsonLoadFile( "$saves:COMObjectEditorSave.json", scene );

		foreach( auto param : scene.m_SceneObjects )
		{
			Object object = GetGame().CreateObject( param.param1, param.param2, false, false );
			object.SetOrientation( param.param3 );
			COM_ForceTargetCollisionUpdate( object );

			m_Objects.Insert( object );
		}
	}

	void EditorState( bool state )
	{
		if ( m_ObjectEditorActive == state )
		{
			return;
		}

		m_ObjectEditorActive = state;

		if ( m_ObjectEditorActive )
		{
			COM_GetPB().MessageStatus("Object Editor Enabled");
		}
		else
		{
			COM_GetPB().MessageStatus("Object Editor Disabled");
		}
	}

	void ToggleEditor()
	{
		m_ObjectEditorActive = !m_ObjectEditorActive;

		if ( m_ObjectEditorActive )
		{
			COM_GetPB().MessageStatus("Object Editor Enabled");
		}
		else
		{
			COM_GetPB().MessageStatus("Object Editor Disabled");
		}
	}

	bool IsEditing()
	{
		return m_ObjectEditorActive;
	}

	void SelectObject( Object object )
	{
		if ( ( ( m_SelectedObject != NULL ) && ( m_SelectedObject == object ) ) || object.IsInherited( PlayerBase ) )
		{
			return;
		}

		if ( COM_CTRL() )
		{
			vector modelPos = m_SelectedObject.WorldToModel( object.GetPosition() );

			ObjectInfoMenu.infoPosX.SetText( modelPos[0].ToString() );
			ObjectInfoMenu.infoPosY.SetText( modelPos[1].ToString() );
			ObjectInfoMenu.infoPosZ.SetText( modelPos[2].ToString() );

		}
		else
		{
			ObjectInfoMenu.infoPosX.SetText( object.GetPosition()[0].ToString() );
			ObjectInfoMenu.infoPosY.SetText( object.GetPosition()[1].ToString() );
			ObjectInfoMenu.infoPosZ.SetText( object.GetPosition()[2].ToString() );

			ObjectInfoMenu.infoPosYaw.SetText( object.GetOrientation()[0].ToString() );
			ObjectInfoMenu.infoPosPitch.SetText( object.GetOrientation()[1].ToString() );
			ObjectInfoMenu.infoPosRoll.SetText( object.GetOrientation()[2].ToString() );
		}

		m_SelectedObject = object;
	}

	void DeselectObject()
	{
		m_SelectedObject = NULL;
	}

	void DragObject()
	{
		if ( !m_ObjectEditorActive )
		{
			return;
		}

		if ( m_SelectedObject )
		{
			vector dir = GetGame().GetPointerDirection();

			vector from = GetGame().GetCurrentCameraPosition();

			vector to = from + ( dir * 10000 );

			vector contact_pos;
			vector contact_dir;
			vector bbox;

			int contact_component;

			m_SelectedObject.GetCollisionBox( bbox );

			if ( DayZPhysics.RaycastRV( from, to, contact_pos, contact_dir, contact_component, NULL, m_SelectedObject, COM_GetPB(), false, false ) )
			{
				m_SelectedObject.SetPosition( contact_pos );
				m_SelectedObject.PlaceOnSurface();

				COM_ForceTargetCollisionUpdate( m_SelectedObject );

				ObjectInfoMenu.infoPosX.SetText( m_SelectedObject.GetPosition()[0].ToString() );
				ObjectInfoMenu.infoPosY.SetText( m_SelectedObject.GetPosition()[1].ToString() );
				ObjectInfoMenu.infoPosZ.SetText( m_SelectedObject.GetPosition()[2].ToString() );
			}
		}
	}

	void ClickObject()
	{
		if ( !m_ObjectEditorActive )
		{
			return;
		}
		Widget widgetCursor = GetGame().GetUIManager().GetWidgetUnderCursor();

		if ( widgetCursor && widgetCursor.GetName() != "EditorMenu" )
		{
			return;
		}

		vector dir = GetGame().GetPointerDirection();
		vector from = GetGame().GetCurrentCameraPosition();
		vector to = from + ( dir * 100 );

		set< Object > objects = COM_GetObjectsAt(from, to, COM_GetPB(), 0.5 );
		bool selected = false;

		if ( objects )
		{
			for ( int newObject = 0; ( ( newObject < objects.Count() ) && !selected ); ++newObject )
			{
				Object obj = objects.Get( newObject );

				if ( COM_CTRL() )
				{

					vector modelPos = obj.WorldToModel( m_SelectedObject.GetPosition() );

					ObjectInfoMenu.infoPosX.SetText( modelPos[0].ToString() );
					ObjectInfoMenu.infoPosY.SetText( modelPos[1].ToString() );
					ObjectInfoMenu.infoPosZ.SetText( modelPos[2].ToString() );
				}
				else
				{
					SelectObject( obj );
					selected = true;

					ObjectInfoMenu.infoPosX.SetText( m_SelectedObject.GetPosition()[0].ToString() );
					ObjectInfoMenu.infoPosY.SetText( m_SelectedObject.GetPosition()[1].ToString() );
					ObjectInfoMenu.infoPosZ.SetText( m_SelectedObject.GetPosition()[2].ToString() );

					ObjectInfoMenu.infoPosYaw.SetText( m_SelectedObject.GetOrientation()[0].ToString() );
					ObjectInfoMenu.infoPosPitch.SetText( m_SelectedObject.GetOrientation()[1].ToString() );
					ObjectInfoMenu.infoPosRoll.SetText( m_SelectedObject.GetOrientation()[2].ToString() );
				}
			}
		}

		if ( !selected && m_SelectedObject )
		{
			COM_GetPB().MessageStatus("Current object deselected.");
			DeselectObject();
		}
	}

	void DeleteObject()
	{
		if ( !m_ObjectEditorActive )
		{
			return;
		}

		if ( m_SelectedObject )
		{
			m_Objects.RemoveItem(m_SelectedObject);
			m_SelectedObject.SetPosition(vector.Zero); // If object does not physically delete, teleport it to 0 0 0
			GetGame().ObjectDelete( m_SelectedObject );

			ObjectInfoMenu.UpdateObjectList();

			m_SelectedObject = NULL;
		}
	}

	void GroundObject()
	{
		if ( !m_ObjectEditorActive )
		{
			return;
		}

		if ( m_SelectedObject )
		{
			m_SelectedObject.PlaceOnSurface();
		}
	}
}
