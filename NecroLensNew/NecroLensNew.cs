#undef DEBUG


using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Dalamud.Game;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using NecroLensNew.Model;
using NecroLensNew.Service;
using NecroLensNew.Windows;

namespace NecroLensNew;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
[SuppressMessage("ReSharper", "UnusedType.Global")]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public sealed class NecroLensNew : IDalamudPlugin
{
    private readonly ConfigWindow configWindow;
    private readonly DeepDungeonService deepDungeonService;
    private readonly ESPService espService;
    private readonly MainWindow mainWindow;
    private readonly MapWindow mapWindow;
    private readonly MapDataService mapDataService;
    private readonly TreasureSpotService spotService;
    private readonly MobInfoService mobInfoService;
    private readonly PluginCommands pluginCommands;

    public readonly WindowSystem WindowSystem = new("NecroLensNew");

#if DEBUG
    private readonly ESPTestService espTestService;
#endif

    public NecroLensNew(IDalamudPluginInterface? pluginInterface)
    {
        pluginInterface?.Create<PluginService>();
        Plugin = this;

        ECommonsMain.Init(pluginInterface, this, Module.DalamudReflector);

        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        pluginCommands = new PluginCommands();
        configWindow = new ConfigWindow();
        mainWindow = new MainWindow();
        mapWindow = new MapWindow();

        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(mapWindow);

        mobInfoService = new MobInfoService();
        MobService = mobInfoService;

        espService = new ESPService();

        deepDungeonService = new DeepDungeonService();
        DungeonService = deepDungeonService;

        mapDataService = new MapDataService();
        MapService = mapDataService;

        spotService = new TreasureSpotService();
        SpotService = spotService;
#if DEBUG
        espTestService = new ESPTestService();
#endif
        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ShowConfigWindow;

        if (Config.Language == "")
        {
            CultureInfo.DefaultThreadCurrentUICulture = ClientState.ClientLanguage switch
            {
                ClientLanguage.French => CultureInfo.GetCultureInfo("fr"),
                ClientLanguage.German => CultureInfo.GetCultureInfo("de"),
                ClientLanguage.Japanese => CultureInfo.GetCultureInfo("ja"),
                ClientLanguage.ChineseSimplified => CultureInfo.GetCultureInfo("zh"),
                _ => CultureInfo.GetCultureInfo("en")
            };
        }
        else
        {
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(Config.Language);
        }
        // DefaultThreadCurrentUICulture does not retroactively update the
        // render thread - set it explicitly.
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture;
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();

        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= ShowConfigWindow;

        configWindow.Dispose();
        pluginCommands.Dispose();
        mainWindow.Dispose();
        mapWindow.Dispose();
        mapDataService.Dispose();
        spotService.Dispose();
        espService.Dispose();
        deepDungeonService.Dispose();
#if DEBUG
        espTestService.Dispose();
#endif
        mobInfoService.Dispose();
        
        ECommonsMain.Dispose();
    }

    private void DrawUI()
    {
        WindowSystem.Draw();
    }

    public void ShowMainWindow()
    {
        mainWindow.IsOpen = true;
    }

    public void CloseMainWindow()
    {
        mainWindow.IsOpen = false;
    }

    public void ShowMapWindow()
    {
        mapWindow.IsOpen = true;
    }

    public void CloseMapWindow()
    {
        mapWindow.IsOpen = false;
    }

    public void ShowConfigWindow()
    {
        configWindow.IsOpen = true;
    }
}
