using System;
using Dalamud.Game.Command;
using NecroLensNew.Data;
using NecroLensNew.util;

namespace NecroLensNew;

public class PluginCommands : IDisposable
{
    public PluginCommands()
    {
        CommandManager.AddHandler("/NecroLensNew",
            new CommandInfo((_, _) => Plugin.ShowMainWindow())
            {
                HelpMessage = Strings.PluginCommands_OpenOverlay_Help,
                ShowInHelp = true
            });

        CommandManager.AddHandler("/NecroLensNewcfg",
            new CommandInfo((_, _) => Plugin.ShowConfigWindow())
            {
                HelpMessage = Strings.PluginCommands_OpenConfig_Help,
                ShowInHelp = true
            });
        
        CommandManager.AddHandler("/openchest",
            new CommandInfo((_, _) => DungeonService.TryNearestOpenChest())
            {
                HelpMessage = Strings.PluginCommands_OpenChest_Help,
                ShowInHelp = true
            });
        
        CommandManager.AddHandler("/pomander",
            new CommandInfo((_, args) => DungeonService.OnPomanderCommand(args))
            {
                HelpMessage = "Try to use the pomander with given name",
                ShowInHelp = true
            });

        CommandManager.AddHandler("/nlnewmap",
            new CommandInfo((_, _) => Plugin.ShowMapWindow())
            {
                HelpMessage = "Open the floor map window",
                ShowInHelp = true
            });

        CommandManager.AddHandler("/nlnew",
            new CommandInfo((_, args) => OnNlNewCommand(args))
            {
                HelpMessage = "NecroLensNew: '/nlnew mark [n]' records a treasure spot, '/nlnew unmark' removes the nearest, '/nlnew spots' shows stats",
                ShowInHelp = true
            });
    }

    private void OnNlNewCommand(string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            Plugin.ShowMapWindow();
            return;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "mark":
                var order = parts.Length > 1 && int.TryParse(parts[1], out var o) ? o : -1;
                SpotService.MarkPlayerSpot(order);
                DeepDungeonUtil.PrintChatMessage($"Treasure spot recorded at your position{(order >= 0 ? $" (order {order})" : "")}");
                break;
            case "unmark":
                DeepDungeonUtil.PrintChatMessage(SpotService.RemoveNearestSpot()
                                                     ? "Nearest treasure spot removed."
                                                     : "No treasure spot found nearby.");
                break;
            case "spots":
                var list = SpotService.CurrentSpots();
                DeepDungeonUtil.PrintChatMessage(
                    $"Territory {ClientState.TerritoryType} layout {MapService.Snapshot.ActiveLayoutIndex}: " +
                    $"{list?.Count ?? 0} spots (total {SpotService.TotalSpots()})");
                break;
            default:
                DeepDungeonUtil.PrintChatMessage("Unknown subcommand. Use '/nlnew mark [n]' or '/nlnew spots'.");
                break;
        }
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler("/NecroLensNew");
        CommandManager.RemoveHandler("/NecroLensNewcfg");
        CommandManager.RemoveHandler("/openchest");
        CommandManager.RemoveHandler("/pomander");
        CommandManager.RemoveHandler("/nlnewmap");
        CommandManager.RemoveHandler("/nlnew");
    }
}
