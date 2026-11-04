using System;

namespace NecroLensNew.Model;

/**
 * A candidate spawn slot shared by traps and the accursed hoard.
 * Stored per (territory, activeLayoutIndex).
 */
[Serializable]
public class TreasureSpot
{
    public float X;
    public float Y;
    public float Z;

    /**
     * Guide numbering (0,1,2,3...) used by the "N trap + 1" rule.
     * -1 when not yet assigned.
     */
    public int Order = -1;

    /** bit1 = seen as trap, bit2 = seen as hoard, bit4 = manual mark */
    public int Mask;

    /** Floor where the spot was first recorded */
    public int Floor;

    /** 5x5 grid room index; filled later once world->cell calibration exists */
    public int Room = -1;

    /**
     * Runtime-only state, reset every floor (slots re-roll):
     * bit1 = live trap object seen this floor, bit2 = live hoard this floor,
     * bit4 = player stood on the slot and nothing happened.
     */
    [Newtonsoft.Json.JsonIgnore]
    public int Live;

    /** When the player first stood on this slot (for empty-check timing). */
    [Newtonsoft.Json.JsonIgnore]
    public DateTime StandSince;
}
