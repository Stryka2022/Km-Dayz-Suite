/*
	Persistency path globals + PersistencyPrint helper.

	These originally lived in COM's Persistency/module.c (which is NOT ported: its
	PersistencyModule / character menus depend on UIExtender & CustomInGameMenu that
	don't exist in this COM copy, and it owned player spawning which the mission
	init.c owns here). Only the self-contained Data/Load/Save helper classes are
	ported (CharacterSave/CharacterLoad/ItemSave/ItemLoad/...); they need these
	globals, so they are defined here.

	BASE_COM_DIR points at the mod's $saves subfolder (port rule #6: keep $saves:).
*/
string BASE_COM_DIR = "$saves:DzlDevTools";
string BASE_PLAYER_SAVE_DIR = BASE_COM_DIR + "\\PlayerSaves";

void PersistencyPrint( string var )
{
    if ( true )
    {
        Print( var );
    }
}
