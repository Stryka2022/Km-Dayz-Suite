// Ported from COM core/modules/Persistency/Load/InventoryLoad.c (unchanged).
class InventoryLoad : ItemLoad
{
    static override EntityAI Load(EntityAI oParent, PlayerBase oPlayer, ItemData oData) {
        if (oData.NumAttachments > 0)
        {
            for (int iAttachment = 0; iAttachment < oData.NumAttachments; iAttachment++)
            {
                FixAndLoadAttachment(oParent, oPlayer, oData.ItemAttachments[iAttachment]);
            }
        }

        return NULL;
    }
}
