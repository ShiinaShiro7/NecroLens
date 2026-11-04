using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Newtonsoft.Json;
using NecroLensNew.Model;
using NecroLensNew.util;

namespace NecroLensNew.Service;

/**
 * Collects and persists trap/hoard candidate spawn positions.
 * Positions are keyed by (territory, activeLayoutIndex) since each of the
 * two floor templates exists as a separate mirrored world copy.
 */
public class TreasureSpotService : IDisposable
{
    private const float MergeDistance = 1.5f;
    private const int MaskTrap = 1;
    private const int MaskHoard = 2;
    private const int MaskManual = 4;
    public const int LiveTrap = 1;
    public const int LiveHoard = 2;
    public const int LiveChecked = 4;

    private readonly string filePath;
    private readonly string calibFilePath;
    private Dictionary<uint, Dictionary<int, List<TreasureSpot>>> db = new();
    private Dictionary<uint, Dictionary<int, GridCalibration>> calibs = new();
    private bool dirty;
    private DateTime lastSave = DateTime.Now;
    private DateTime lastSample = DateTime.MinValue;
    private uint liveTerritory;
    private int liveFloor = -1;

    public TreasureSpotService()
    {
        filePath = Path.Combine(PluginInterface.ConfigFile.Directory!.FullName, "treasureSpots.json");
        calibFilePath = Path.Combine(PluginInterface.ConfigFile.Directory!.FullName, "gridCalib.json");
        Load();
        Framework.Update += OnTick;
        ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        Framework.Update -= OnTick;
        ClientState.TerritoryChanged -= OnTerritoryChanged;
        Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(filePath))
                db = JsonConvert.DeserializeObject<Dictionary<uint, Dictionary<int, List<TreasureSpot>>>>(
                         File.ReadAllText(filePath)) ?? new();
            if (File.Exists(calibFilePath))
            {
                calibs = JsonConvert.DeserializeObject<Dictionary<uint, Dictionary<int, GridCalibration>>>(
                             File.ReadAllText(calibFilePath)) ?? new();
                foreach (var layouts in calibs.Values)
                foreach (var calib in layouts.Values)
                    calib.Recompute();
            }

            PluginLog.Information($"Treasure spots loaded: {filePath}");
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "Failed to load treasureSpots.json");
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(filePath, JsonConvert.SerializeObject(db, Formatting.Indented));
            File.WriteAllText(calibFilePath, JsonConvert.SerializeObject(calibs, Formatting.Indented));
            dirty = false;
            lastSave = DateTime.Now;
        }
        catch (Exception e)
        {
            PluginLog.Error(e, "Failed to save treasureSpots.json");
        }
    }

    private void OnTerritoryChanged(uint territory)
    {
        if (dirty) Save();
    }

    private void OnTick(IFramework framework)
    {
        try
        {
            if (!DeepDungeonUtil.InDeepDungeon || !MapService.Snapshot.Valid)
                return;

            // Live trap/hoard objects drive the N+1 rule even when spot
            // collection is off. Slots re-roll each floor: reset on change.
            var territory = ClientState.TerritoryType;
            var floor = MapService.Snapshot.Floor;
            if (territory != liveTerritory || floor != liveFloor)
            {
                liveTerritory = territory;
                liveFloor = floor;
                foreach (var layouts in db.Values)
                foreach (var list in layouts.Values)
                foreach (var s in list)
                    s.Live = 0;
            }

            var collect = Config.CollectTreasureSpots;
            foreach (var obj in ObjectTable)
            {
                var id = obj.BaseId;
                var mask = id == DataIds.AccursedHoard || id == DataIds.AccursedHoardCoffer
                               ? MaskHoard
                               : DataIds.TrapIDs.ContainsKey(id)
                                   ? MaskTrap
                                   : 0;
                if (mask == 0) continue;
                if (mask == MaskHoard)
                    // A hoard object in the world means this floor's hoard
                    // was revealed (Intuition) or dug up.
                    DungeonService.FloorDetails.FloorHoardConfirmed = true;
                var spot = collect ? RecordSpot(obj.Position, mask) : FindSpot(obj.Position);
                if (spot != null) spot.Live |= mask;
            }

            // Stepped-on-a-slot check: ~3s inside the trigger area (movement
            // within it counts) with no trap/hoard appearing -> checked-empty.
            var player = ObjectTable.LocalPlayer;
            var spots = CurrentSpots();
            if (player != null && spots != null)
            {
                var now = DateTime.Now;
                foreach (var spot in spots)
                {
                    if ((spot.Live & (LiveTrap | LiveHoard | LiveChecked)) != 0) continue;
                    var dx = spot.X - player.Position.X;
                    var dz = spot.Z - player.Position.Z;
                    var near = dx * dx + dz * dz <= 4.0f;
                    if (!near)
                    {
                        spot.StandSince = default;
                        continue;
                    }
                    if (spot.StandSince == default)
                        spot.StandSince = now;
                    else if ((now - spot.StandSince).TotalSeconds > 3.2)
                        spot.Live |= LiveChecked;
                }
            }

            if (!collect)
                return;
            if (player != null && MapService.Snapshot.PlayerRoomIndex >= 0 &&
                (DateTime.Now - lastSample).TotalMilliseconds > 500)
            {
                GetCalib(ClientState.TerritoryType, MapService.Snapshot.ActiveLayoutIndex)
                    .AddSample(MapService.Snapshot.PlayerRoomIndex, player.Position.X, player.Position.Z);
                lastSample = DateTime.Now;
            }

            // Persist room assignment for spots collected before the grid
            // converged. Without this, borderline spots flicker in and out
            // of the tolerance radius as each refit shifts the cell centers.
            var cur = CurrentSpots();
            if (cur != null)
            {
                foreach (var spot in cur)
                {
                    // Corner cells are never real rooms: reassign legacy spots
                    // that were mistakenly mapped onto them.
                    var badRoom = spot.Room is 0 or 4 or 20 or 24;
                    if (spot.Room >= 0 && !badRoom) continue;
                    var room = AssignRoom(spot.X, spot.Z);
                    if (room != spot.Room)
                    {
                        spot.Room = room;
                        dirty = true;
                    }
                }
            }

            if (dirty && (DateTime.Now - lastSave).TotalSeconds > 20)
                Save();
        }
        catch (Exception e)
        {
            PluginLog.Error(e.ToString());
        }
    }

    private TreasureSpot RecordSpot(Vector3 pos, int mask)
    {
        var territory = ClientState.TerritoryType;
        var layout = MapService.Snapshot.ActiveLayoutIndex;

        if (!db.TryGetValue(territory, out var layouts))
            db[territory] = layouts = new Dictionary<int, List<TreasureSpot>>();
        if (!layouts.TryGetValue(layout, out var list))
            layouts[layout] = list = new List<TreasureSpot>();

        foreach (var spot in list)
        {
            if (Math.Abs(spot.X - pos.X) < MergeDistance && Math.Abs(spot.Z - pos.Z) < MergeDistance)
            {
                var newMask = spot.Mask | mask;
                if (newMask != spot.Mask)
                {
                    spot.Mask = newMask;
                    dirty = true;
                }
                return spot;
            }
        }

        var created = new TreasureSpot
        {
            X = pos.X, Y = pos.Y, Z = pos.Z,
            Mask = mask,
            Floor = MapService.Snapshot.Floor,
            Room = AssignRoom(pos.X, pos.Z)
        };
        list.Add(created);
        dirty = true;
        PluginLog.Debug($"New treasure spot T{territory} L{layout} ({pos.X:F1},{pos.Y:F1},{pos.Z:F1}) mask={mask}");
        return created;
    }

    /**
     * /nlnew mark [order] - record the player's current position as a spot.
     */
    public void MarkPlayerSpot(int order)
    {
        var player = ObjectTable.LocalPlayer;
        if (player == null) return;

        var spot = RecordSpot(player.Position, MaskManual);
        // Manual marks are at the player's feet - their room is exact.
        if (MapService.Snapshot.PlayerRoomIndex >= 0)
            spot.Room = MapService.Snapshot.PlayerRoomIndex;
        if (order >= 0)
        {
            spot.Order = order;
            dirty = true;
        }
        Save();
    }

    private GridCalibration GetCalib(uint territory, int layout)
    {
        if (!calibs.TryGetValue(territory, out var layouts))
            calibs[territory] = layouts = new Dictionary<int, GridCalibration>();
        if (!layouts.TryGetValue(layout, out var calib))
            layouts[layout] = calib = new GridCalibration();
        return calib;
    }

    public int AssignRoom(float x, float z)
    {
        // Exact LGB room centers first - zero calibration drift.
        if (LgbRoomCenters.Data.TryGetValue(ClientState.TerritoryType, out var lgbLayouts) &&
            lgbLayouts.TryGetValue(MapService.Snapshot.ActiveLayoutIndex, out var centers))
        {
            var best = -1;
            var bestDist = float.MaxValue;
            foreach (var (i, c) in centers)
            {
                var d = (c.X - x) * (c.X - x) + (c.Z - z) * (c.Z - z);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            if (best >= 0) return best;
        }

        return calibs.TryGetValue(ClientState.TerritoryType, out var layouts) &&
               layouts.TryGetValue(MapService.Snapshot.ActiveLayoutIndex, out var calib)
                   ? calib.TryAssignRoom(x, z)
                   : -1;
    }

    /**
     * Per-cell collected spot counts for the map window.
     * Prefers the spot's recorded Room, falls back to live calibration.
     */
    public Dictionary<int, int> CellSpotCounts()
    {
        var counts = new Dictionary<int, int>();
        var list = CurrentSpots();
        if (list == null) return counts;

        foreach (var spot in list)
        {
            var room = spot.Room >= 0 ? spot.Room : AssignRoom(spot.X, spot.Z);
            if (room >= 0 && room < 25 && room is not (0 or 4 or 20 or 24))
                counts[room] = (counts.TryGetValue(room, out var n) ? n : 0) + 1;
        }
        return counts;
    }

    /**
     * /nlnew unmark - removes the spot nearest to the player.
     */
    public bool RemoveNearestSpot(float maxDistance = 5f)
    {
        var player = ObjectTable.LocalPlayer;
        var list = CurrentSpots();
        if (player == null || list == null) return false;

        TreasureSpot? best = null;
        var bestDist = float.MaxValue;
        foreach (var spot in list)
        {
            var dist = pos2(spot);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = spot;
            }
        }

        if (best == null || bestDist > maxDistance) return false;

        list.Remove(best);
        dirty = true;
        Save();
        return true;

        float pos2(TreasureSpot s)
        {
            var dx = s.X - player.Position.X;
            var dz = s.Z - player.Position.Z;
            return dx * dx + dz * dz;
        }
    }

    /**
     * Spots for the current territory + active layout, or null.
     */
    public List<TreasureSpot>? CurrentSpots()
    {
        return db.TryGetValue(ClientState.TerritoryType, out var layouts) &&
               layouts.TryGetValue(MapService.Snapshot.ActiveLayoutIndex, out var list)
                   ? list
                   : null;
    }

    public int TotalSpots()
    {
        var n = 0;
        foreach (var layouts in db.Values)
        foreach (var list in layouts.Values)
            n += list.Count;
        return n;
    }

    /**
     * Nearest persisted spot within merge distance (no creation).
     */
    private TreasureSpot? FindSpot(Vector3 pos)
    {
        var list = CurrentSpots();
        if (list == null) return null;
        TreasureSpot? best = null;
        var bestDist = MergeDistance;
        foreach (var spot in list)
        {
            var d = MathF.Max(MathF.Abs(spot.X - pos.X), MathF.Abs(spot.Z - pos.Z));
            if (d < bestDist)
            {
                bestDist = d;
                best = spot;
            }
        }
        return best;
    }

    public sealed class RuleView
    {
        public int HomeRoom = -1;
        public bool HideAll;
        public readonly bool[] RoomPresent = new bool[DDMapSnapshot.RoomCount];
        public readonly Dictionary<TreasureSpot, int> SpotRooms = new();
        public readonly HashSet<int> ResolvedRooms = new();
        public readonly Dictionary<int, HashSet<int>> AllowedOrders = new();
        public readonly HashSet<int> HotOrders = new();
        public float HomeX, HomeZ, HomeRadius2;
    }

    /**
     * Orders worth stepping on at this floor-in-set, per the guide's
     * probability ramp (early floors favor order 1, higher floors spread).
     */
    private static HashSet<int> HotOrdersForFloor(int floor)
    {
        return (floor % 10) switch
        {
            1 or 2 or 3 => new HashSet<int> { 0, 1 },
            4 or 5 or 6 => new HashSet<int> { 0, 1, 2 },
            _ => new HashSet<int> { 0, 1, 2, 3 }
        };
    }

    /**
     * Per-room N+1 rule state built from live trap/hoard objects seen this
     * floor. Only meaningful when spot collection is off (play mode).
     */
    public RuleView GetRuleView()
    {
        var view = new RuleView();
        var fd = DungeonService.FloorDetails;
        view.HideAll = fd.HoardFound || fd.FloorHoardConfirmed || fd.IntuitionActive;
        view.HotOrders.UnionWith(HotOrdersForFloor(MapService.Snapshot.Floor));
        for (var i = 0; i < DDMapSnapshot.RoomCount; i++)
        {
            var flags = MapService.Snapshot.Rooms[i];
            view.RoomPresent[i] = flags != InstanceContentDeepDungeon.RoomFlags.None;
            if ((flags & InstanceContentDeepDungeon.RoomFlags.Home) != 0)
                view.HomeRoom = i;
        }

        // Distance fallback for the start room: covers spots whose grid
        // assignment failed or landed on the wrong cell.
        if (view.HomeRoom >= 0 &&
            calibs.TryGetValue(ClientState.TerritoryType, out var cl) &&
            cl.TryGetValue(MapService.Snapshot.ActiveLayoutIndex, out var cal) && cal.Valid)
        {
            var (hx, hz) = cal.Center(view.HomeRoom);
            view.HomeX = hx;
            view.HomeZ = hz;
            var pitch = MathF.Max(MathF.Abs(cal.PitchX), MathF.Abs(cal.PitchZ));
            view.HomeRadius2 = pitch * pitch * 0.49f;
        }

        var list = CurrentSpots();
        if (list == null) return view;
        foreach (var spot in list)
        {
            // Live grid assignment over the stored room: early calibration
            // could pin a spot to the wrong cell.
            var room = AssignRoom(spot.X, spot.Z);
            if (room < 0) room = spot.Room;
            view.SpotRooms[spot] = room;
            if (room < 0) continue;
            if ((spot.Live & LiveHoard) != 0) view.ResolvedRooms.Add(room);
            if ((spot.Live & LiveTrap) != 0 && spot.Order >= 0)
            {
                if (!view.AllowedOrders.TryGetValue(room, out var set))
                    view.AllowedOrders[room] = set = new HashSet<int>();
                set.Add(spot.Order + 1);
            }
        }
        return view;
    }

    /**
     * Draw classification for a spot: null = hidden, otherwise
     * 0 = normal, 1 = N+1 candidate, 2 = ruled out, 3 = live trap.
     */
    public static int? DrawState(TreasureSpot spot, RuleView? view)
    {
        if (view == null) return 0;
        if (view.HideAll || (spot.Live & LiveHoard) != 0) return null;
        var room = view.SpotRooms.TryGetValue(spot, out var r) ? r : spot.Room;
        if (room == view.HomeRoom || view.ResolvedRooms.Contains(room)) return null;
        if (view.HomeRadius2 > 0)
        {
            var dx = spot.X - view.HomeX;
            var dz = spot.Z - view.HomeZ;
            if (dx * dx + dz * dz < view.HomeRadius2) return null;
        }
        if (room >= 0 && room < view.RoomPresent.Length && !view.RoomPresent[room])
            return null;
        if ((spot.Live & LiveTrap) != 0) return 3;
        if ((spot.Live & LiveChecked) != 0) return null;
        if (view.AllowedOrders.TryGetValue(room, out var orders))
            return orders.Contains(spot.Order) ? 1 : 2;
        if (spot.Order >= 0 && !view.HotOrders.Contains(spot.Order)) return 4;
        return 0;
    }

    /**
     * Spots positioned inside their map cell as normalized offsets
     * (u,v in cell units). Empty unless the grid is calibrated.
     */
    public List<(TreasureSpot Spot, float U, float V)> SpotsForMap()
    {
        var result = new List<(TreasureSpot, float, float)>();
        var list = CurrentSpots();
        if (list == null ||
            !calibs.TryGetValue(ClientState.TerritoryType, out var layouts) ||
            !layouts.TryGetValue(MapService.Snapshot.ActiveLayoutIndex, out var calib) ||
            !calib.Valid || calib.PitchX == 0 || calib.PitchZ == 0)
            return result;

        foreach (var spot in list)
        {
            if (spot.Room < 0) continue;
            var (cx, cz) = calib.Center(spot.Room);
            var u = Math.Clamp((spot.X - cx) / calib.PitchX, -0.7f, 0.7f);
            var v = Math.Clamp((spot.Z - cz) / calib.PitchZ, -0.7f, 0.7f);
            result.Add((spot, u, v));
        }
        return result;
    }
}
