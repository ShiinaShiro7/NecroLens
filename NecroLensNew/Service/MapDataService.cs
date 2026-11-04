using System;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using NecroLensNew.Model;
using NecroLensNew.util;

namespace NecroLensNew.Service;

/**
 * Reads InstanceContentDeepDungeon every framework tick and keeps a
 * managed snapshot for the map window / overlay drawing.
 */
public unsafe class MapDataService : IDisposable
{
    public readonly DDMapSnapshot Snapshot = new();
    private uint lastTerritory;
    private byte lastFloor;
    private byte lastLayout;

    public MapDataService()
    {
        Framework.Update += OnTick;
    }

    public void Dispose()
    {
        Framework.Update -= OnTick;
    }

    private void OnTick(IFramework framework)
    {
        try
        {
            var dd = EventFramework.Instance()->GetInstanceContentDeepDungeon();
            if (dd == null || !DeepDungeonUtil.InDeepDungeon)
            {
                Snapshot.Valid = false;
                return;
            }

            Snapshot.Valid = true;
            var map = dd->MapData;
            for (var i = 0; i < DDMapSnapshot.RoomCount; i++)
                Snapshot.Rooms[i] = map[i];

            Snapshot.ActiveLayoutIndex = dd->ActiveLayoutIndex;
            Snapshot.LayoutInitializationType = dd->LayoutInitializationType;
            Snapshot.Floor = dd->Floor;
            Snapshot.HoardCount = dd->HoardCount;
            Snapshot.PassageProgress = dd->PassageProgress;
            Snapshot.ReturnProgress = dd->ReturnProgress;

            var localId = ObjectTable.LocalPlayer?.EntityId ?? 0;
            Snapshot.PlayerRoomIndex = -1;
            Snapshot.PartyRoomIndices.Clear();
            foreach (var member in dd->Party)
            {
                if (member.RoomIndex < 0) continue;
                if (member.EntityId == localId)
                    Snapshot.PlayerRoomIndex = member.RoomIndex;
                else
                    Snapshot.PartyRoomIndices.Add(member.RoomIndex);
            }

            Snapshot.Chests.Clear();
            foreach (var chest in dd->Chests)
            {
                if (chest.RoomIndex >= 0)
                    Snapshot.Chests.Add((chest.ChestType, chest.RoomIndex));
            }

            if (ClientState.TerritoryType != lastTerritory ||
                dd->Floor != lastFloor ||
                dd->ActiveLayoutIndex != lastLayout)
            {
                lastTerritory = ClientState.TerritoryType;
                lastFloor = dd->Floor;
                lastLayout = dd->ActiveLayoutIndex;
                DumpSnapshot(dd);
            }
        }
        catch (Exception e)
        {
            PluginLog.Error(e.ToString());
        }
    }

    /**
     * Raw data dump on each floor/layout change.
     */
    private void DumpSnapshot(FFXIVClientStructs.FFXIV.Client.Game.InstanceContent.InstanceContentDeepDungeon* dd)
    {
        var sb = new StringBuilder();
        sb.Append($"[DDMap] T{ClientState.TerritoryType} F{dd->Floor} layout={dd->ActiveLayoutIndex} " +
                  $"initType={dd->LayoutInitializationType} hoard={dd->HoardCount} " +
                  $"passage={dd->PassageProgress} return={dd->ReturnProgress}\n rooms:");
        for (var i = 0; i < DDMapSnapshot.RoomCount; i++)
            sb.Append($" {i}={(ushort)dd->MapData[i]:X}");
        sb.Append("\n chests:");
        foreach (var chest in dd->Chests)
            sb.Append($" {chest.ChestType}@{chest.RoomIndex}");
        sb.Append("\n party:");
        foreach (var member in dd->Party)
            sb.Append($" {member.EntityId:X8}@{member.RoomIndex}");
        PluginLog.Information(sb.ToString());
    }
}
