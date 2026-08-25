/*
	Ported from COM core/modules/BarrelCrosshair/CustomFirstPersonCamera.c (unchanged).
	modded class DayZPlayerCamera1stPerson; overrides the 1st-person bone to
	Face_Forehead. GetBoneIndexByName verified vanilla on DayZPlayer.
*/
modded class DayZPlayerCamera1stPerson
{
    void DayZPlayerCamera1stPerson( DayZPlayer pPlayer, HumanInputController pInput )
    {
        m_iBoneIndex = pPlayer.GetBoneIndexByName("Face_Forehead");
        if (m_iBoneIndex == -1)
        {
            Print("modded DayZPlayerCamera1stPerson: main bone not found");
        }
    }
}
