using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Kupopop;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    private readonly WindowSystem windows = new("Kupopop");
    private readonly MainWindow main;
    private readonly DiagnosticLog diagnostic;
    private long lastFrameFailure;
    internal Configuration Config { get; }

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new();
        string directory = PluginInterface.GetPluginConfigDirectory();
        diagnostic = new(directory);
        diagnostic.Write($"plugin.start version=0.1.0.0 api=15 runtime={Environment.Version}");
        main = new(this, directory, diagnostic) { IsOpen = true };
        windows.AddWindow(main);
        CommandManager.AddHandler("/kupopop", new CommandInfo((_, args) =>
        {
            main.IsOpen = true;
            if (args.Trim().Equals("debug", StringComparison.OrdinalIgnoreCase)) main.ShowDebug = !main.ShowDebug;
        }) { HelpMessage = "Open Kupopop Draw. Use /kupopop debug to show diagnostic information." });
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenMainUi += Open;
        PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
        Log.Information("Kupopop Draw loaded. Diagnostics: {Path}", diagnostic.FilePath);
    }

    internal void Save() => PluginInterface.SavePluginConfig(Config);
    private void Draw()
    {
        try { main.PollNetwork(); windows.Draw(); }
        catch (Exception ex)
        {
            if (Environment.TickCount64 - lastFrameFailure > 5000)
            { diagnostic.Write("plugin.frame_failed", ex); lastFrameFailure = Environment.TickCount64; }
        }
    }
    private void Open() => main.IsOpen = true;
    private void OpenSettings() { main.IsOpen = true; main.ShowDebug = true; }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Open;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        CommandManager.RemoveHandler("/kupopop");
        windows.RemoveAllWindows();
        main.Dispose();
        diagnostic.Write("plugin.stop");
    }
}
