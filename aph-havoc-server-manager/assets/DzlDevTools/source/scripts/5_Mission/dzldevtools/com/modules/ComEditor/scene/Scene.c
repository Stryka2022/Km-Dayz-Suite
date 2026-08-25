// Ported from COM core/modules/ComEditor/scene/Scene.c (unchanged).
// SceneData is a vanilla 4_World type (P:\scripts\4_world\classes\sceneeditor\scenedata.c).
class Scene
{
	ref array<Object> s_Objects = new array<Object>; // All active spawned objects
	ref SceneData sceneData;

	void Scene( SceneData data )
	{
		sceneData = data;
	}

	void ~Scene()
	{
	}
}
