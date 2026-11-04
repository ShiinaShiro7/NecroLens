using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace NecroLensNew.Model;

/**
 * Snapshot of the client-side deep dungeon map layout taken from
 * InstanceContentDeepDungeon. The server sends the full generated
 * layout on floor entry; the Revealed flag only controls the
 * vanilla map UI, so we can render the whole map without a
 * Pomander of Sight.
 */
public class DDMapSnapshot
{
    public const int GridSize = 5;
    public const int RoomCount = GridSize * GridSize;

    public bool Valid;
    public readonly InstanceContentDeepDungeon.RoomFlags[] Rooms =
        new InstanceContentDeepDungeon.RoomFlags[RoomCount];
    public byte ActiveLayoutIndex;
    public byte LayoutInitializationType;
    public byte Floor;
    public byte HoardCount;
    public byte PassageProgress;
    public byte ReturnProgress;
    public int PlayerRoomIndex = -1;
    public readonly List<int> PartyRoomIndices = new();
    public readonly List<(byte Type, int Room)> Chests = new();

    public static int ToIndex(int row, int col) => row * GridSize + col;
    public static (int Row, int Col) ToRowCol(int index) => (index / GridSize, index % GridSize);
}
