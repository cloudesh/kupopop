using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.ImGuiNotification;
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
    [PluginService] internal static INotificationManager Notifications { get; private set; } = null!;
    private readonly WindowSystem windows = new("Kupopop");
    private readonly MainWindow main;
    private readonly DiagnosticLog diagnostic;
    private readonly BackgroundInvitations backgroundInvitations;
    private readonly List<IActiveNotification> invitationNotices = [];
    private bool disposed;
    private long lastFrameFailure;
    internal Configuration Config { get; }

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new();
        string directory = PluginInterface.GetPluginConfigDirectory();
        diagnostic = new(directory);
        backgroundInvitations = new(directory, diagnostic);
        diagnostic.Write($"plugin.start version={typeof(Plugin).Assembly.GetName().Version} api=15 runtime={Environment.Version}");
        main = new(this, directory, diagnostic) { IsOpen = false };
        windows.AddWindow(main);
        var openCommand = new CommandInfo((_, args) =>
        {
            main.IsOpen = true;
            if (args.Trim().Equals("debug", StringComparison.OrdinalIgnoreCase)) main.ShowDebug = !main.ShowDebug;
        })
        { HelpMessage = "Open Kupo Skribbl. Use /kuposkribbl debug for appearance preferences and file diagnostics." };
        CommandManager.AddHandler("/kuposkribbl", openCommand);
        CommandManager.AddHandler("/kupopop", openCommand);
        CommandManager.AddHandler("/kupo", openCommand);
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenMainUi += Open;
        PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
        Log.Information("Kupo Skribbl loaded. Diagnostics: {Path}", diagnostic.FilePath);
    }

    internal void Save() => PluginInterface.SavePluginConfig(Config);
    private void Draw()
    {
        try
        {
            backgroundInvitations.Tick(Config.BackgroundInvitations, main.IsOpen, ShowInvitation,
                () => { Config.BackgroundInvitations = false; Save(); });
            main.PollNetwork();
            using var style = UiTheme.Apply(main.IsDrawingRoom, Config.DarkMode);
            windows.Draw();
            main.SynchronizeVisibility();
        }
        catch (Exception ex)
        {
            if (Environment.TickCount64 - lastFrameFailure > 5000)
            { diagnostic.Write("plugin.frame_failed", ex); lastFrameFailure = Environment.TickCount64; }
        }
    }
    private void Open() => main.IsOpen = true;
    private void ShowInvitation(Kupopop.Shared.GameInvitation invitation)
    {
        invitationNotices.RemoveAll(n => n.DismissReason is not null);
        var notice = Notifications.AddNotification(new Notification
        {
            Title = "A Kupo Skribbl invitation",
            Content = invitation.From.DisplayName + " invited you to " + invitation.RoomTitle + ". Click to open your book.",
            Type = NotificationType.Info,
            InitialDuration = TimeSpan.FromSeconds(12),
            HardExpiry = DateTime.UtcNow.AddMinutes(1),
        });
        notice.Click += args => { if (!disposed) Open(); args.Notification.DismissNow(); };
        invitationNotices.Add(notice);
    }
    private void OpenSettings() { main.IsOpen = true; main.ShowDebug = true; }

    public void Dispose()
    {
        disposed = true;
        backgroundInvitations.Dispose();
        foreach (var notice in invitationNotices) notice.DismissNow();
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Open;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        CommandManager.RemoveHandler("/kupopop");
        CommandManager.RemoveHandler("/kupo");
        CommandManager.RemoveHandler("/kuposkribbl");
        windows.RemoveAllWindows();
        main.Dispose();
        diagnostic.Write("plugin.stop");
    }
}
