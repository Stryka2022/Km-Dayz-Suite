// Ported from COM core/modules/ComEditor/scene/SceneInfo.c (unchanged).
class SceneInfo
{
	string name;
	ref array< ObjectData > m_objects = new array< ObjectData >;
	ref array< LootSpot > m_lootSpots = new array< LootSpot >;

	bool persistent = false;
	int duration = 0; // in seconds

	void SceneInfo( string sceneName )
	{
		name = sceneName;
	}

	void ~SceneInfo()
	{
	}

	string GetName()
	{
		return name;
	}

	void AddObject( Object object, vector pos )
	{
		m_objects.Insert( new ObjectData( object.GetType(), pos, object.GetOrientation() ));
	}

	void AddLootSpot( vector pos, LootType type )
	{
		m_lootSpots.Insert( new LootSpot( pos, type ) );
	}
}
