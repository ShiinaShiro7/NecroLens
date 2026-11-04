using System;
using System.Drawing;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using NecroLensNew.Model;
using NecroLensNew.Service;
using NecroLensNew.util;

namespace NecroLensNew.Windows;

/**
 * Renders the full deep dungeon floor map from InstanceContentDeepDungeon.MapData
 * without requiring a Pomander of Sight. Unrevealed rooms are drawn dimmed so
 * the real exploration progress stays visible.
 */
public class MapWindow : Window, IDisposable
{
    private const float BaseCellSize = 40f;
    private const float BaseGap = 14f;
    private const float BaseUnits = 5 * BaseCellSize + 4 * BaseGap; // 256

    private static readonly Color ColRoomRevealed = Color.FromArgb(55, 75, 115);
    private static readonly Color ColRoomHidden = Color.FromArgb(35, 45, 65);
    private static readonly Color ColHome = Color.ForestGreen;
    private static readonly Color ColPassage = Color.RoyalBlue;

    public MapWindow() : base("楼层地图###NecroLensNewMapWindow", ImGuiWindowFlags.NoCollapse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(160, 160),
            MaximumSize = new Vector2(2000, 2000)
        };
        RespectCloseHotkey = false;
    }

    public void Dispose() { }

    public override bool DrawConditions()
    {
        return Config.ShowFloorMap &&
               DungeonService.Ready &&
               DeepDungeonUtil.InDeepDungeon &&
               MapService.Snapshot.Valid;
    }

    private static uint ChestColor(byte chestType)
    {
        return chestType switch
        {
            1 => Config.BronzeCofferColor,
            2 => Config.SilverCofferColor,
            3 => Config.GoldCofferColor,
            _ => Color.White.ToUint()
        };
    }

    public override void Draw()
    {
        var snap = MapService.Snapshot;
        var drawList = ImGui.GetWindowDrawList();
        var collect = Config.CollectTreasureSpots;

        var cellCounts = collect ? SpotService.CellSpotCounts() : null;

        ImGui.Text($"F{snap.Floor}");
        if (collect)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($" 模板{(char)('A' + snap.ActiveLayoutIndex)}");
        }
        if (snap.LayoutInitializationType == 6)
        {
            ImGui.SameLine();
            ImGui.TextColored(Color.OrangeRed.ToV4(), " Boss层");
        }
        if (cellCounts != null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($" 收集 {cellCounts.Count}/21");
        }
        if (!collect)
        {
            var fd = DungeonService.FloorDetails;
            ImGui.SameLine();
            if (fd.FloorHoardConfirmed || fd.HoardFound)
                ImGui.TextColored(Color.Gold.ToV4(), " 有宝藏");
            else if (fd.IntuitionActive)
                ImGui.TextColored(Color.Gray.ToV4(), " 无宝藏");
            else
                ImGui.TextDisabled(" 宝藏未知");
        }


        // Grid auto-fits the smaller window dimension
        var avail = ImGui.GetContentRegionAvail();
        var unit = MathF.Min(avail.X, avail.Y) / BaseUnits * Config.MapScale;
        var cellSize = BaseCellSize * unit;
        var gap = BaseGap * unit;
        var step = cellSize + gap;

        var origin = ImGui.GetCursorScreenPos();
        var corridorColor = Color.DimGray.ToUint(0.7f);
        var corridorHalf = cellSize * 0.14f;

        for (var i = 0; i < DDMapSnapshot.RoomCount; i++)
        {
            var flags = snap.Rooms[i];
            if (flags == InstanceContentDeepDungeon.RoomFlags.None) continue;

            var (r, c) = DDMapSnapshot.ToRowCol(i);
            var p = origin + new Vector2(c * step, r * step);
            var center = p + new Vector2(cellSize / 2f, cellSize / 2f);

            if ((flags & InstanceContentDeepDungeon.RoomFlags.ConnectionS) != 0 && r < DDMapSnapshot.GridSize - 1)
                drawList.AddRectFilled(center + new Vector2(-corridorHalf, cellSize / 2f),
                                       center + new Vector2(corridorHalf, cellSize / 2f + gap), corridorColor);
            if ((flags & InstanceContentDeepDungeon.RoomFlags.ConnectionE) != 0 && c < DDMapSnapshot.GridSize - 1)
                drawList.AddRectFilled(center + new Vector2(cellSize / 2f, -corridorHalf),
                                       center + new Vector2(cellSize / 2f + gap, corridorHalf), corridorColor);
        }

        var playerRotation = ObjectTable.LocalPlayer?.Rotation ?? 0f;

        for (var i = 0; i < DDMapSnapshot.RoomCount; i++)
        {
            var flags = snap.Rooms[i];
            var (r, c) = DDMapSnapshot.ToRowCol(i);
            var p = origin + new Vector2(c * step, r * step);
            var center = p + new Vector2(cellSize / 2f, cellSize / 2f);

            // Cells outside this floor's layout still get a faint placeholder
            // so template-wide collection coverage stays visible.
            // The four corners never contain rooms in any template.
            if (flags == InstanceContentDeepDungeon.RoomFlags.None)
            {
                if (!collect || i is 0 or 4 or 20 or 24) continue;
                drawList.AddRectFilled(p, p + new Vector2(cellSize), Color.Gray.ToUint(0.10f), cellSize * 0.15f);
                if (cellCounts != null)
                {
                    var c0 = cellCounts.TryGetValue(i, out var n0) ? n0 : 0;
                    drawList.AddCircleFilled(p + new Vector2(cellSize * 0.85f, cellSize * 0.15f),
                                             cellSize * 0.07f,
                                             (c0 > 0 ? Color.LimeGreen : Color.OrangeRed).ToUint(0.5f));
                }
                continue;
            }

            var revealed = (flags & InstanceContentDeepDungeon.RoomFlags.Revealed) != 0;

            var baseColor = revealed ? ColRoomRevealed : ColRoomHidden;
            var fill = baseColor.ToUint(revealed ? 0.95f : 0.55f);
            var border = revealed ? Color.White.ToUint(0.9f) : Color.Gray.ToUint(0.5f);

            drawList.AddRectFilled(p, p + new Vector2(cellSize), fill, cellSize * 0.15f);

            // Special rooms tint the cell background; multiple flags stack inward
            var inset = 0f;
            void SpecialFill(Color color)
            {
                var p1 = p + new Vector2(inset);
                var p2 = p + new Vector2(cellSize - inset);
                drawList.AddRectFilled(p1, p2, color.ToUint(0.75f), cellSize * 0.12f);
                inset += cellSize * 0.18f;
            }

            if ((flags & InstanceContentDeepDungeon.RoomFlags.Home) != 0) SpecialFill(ColHome);
            if ((flags & InstanceContentDeepDungeon.RoomFlags.Passage) != 0) SpecialFill(ColPassage);

            drawList.AddRect(p, p + new Vector2(cellSize), border, cellSize * 0.15f,
                             ImDrawFlags.None, revealed ? 1.5f : 1f);

            if (Config.MapShowChests)
            {
                var chestIndex = 0;
                foreach (var (type, room) in snap.Chests)
                {
                    if (room != i) continue;
                    var dotPos = center + new Vector2(-cellSize * 0.3f + chestIndex * cellSize * 0.2f,
                                                      cellSize * 0.28f);
                    drawList.AddCircleFilled(dotPos, cellSize * 0.07f, ChestColor(type));
                    chestIndex++;
                }
            }

            // Collection progress: green dot = has spots, red dot = uncollected room
            if (cellCounts != null)
            {
                var count = cellCounts.TryGetValue(i, out var cc) ? cc : 0;
                var dotColor = count > 0 ? Color.LimeGreen.ToUint() : Color.OrangeRed.ToUint(0.85f);
                drawList.AddCircleFilled(p + new Vector2(cellSize * 0.85f, cellSize * 0.15f),
                                         cellSize * 0.07f, dotColor);
            }

            // One marker per room: yellow arrow for the local player,
            // a single blue dot for any party members in that room.
            if (snap.PlayerRoomIndex == i)
            {
                // FFXIV heading 0 faces south (+Z), which is screen-down
                // on the north-up map.
                var dir = new Vector2(MathF.Sin(playerRotation), MathF.Cos(playerRotation));
                var perp = new Vector2(-dir.Y, dir.X);
                var tip = center + dir * (cellSize * 0.3f);
                var back = center - dir * (cellSize * 0.15f);
                drawList.AddTriangleFilled(tip,
                                           back + perp * (cellSize * 0.14f),
                                           back - perp * (cellSize * 0.14f),
                                           Color.Yellow.ToUint());
            }
            else if (snap.PartyRoomIndices.Contains(i))
            {
                drawList.AddCircleFilled(center, cellSize * 0.12f, Color.LightBlue.ToUint());
            }
        }

        ImGui.Dummy(new Vector2(step * DDMapSnapshot.GridSize - gap));
    }
}
