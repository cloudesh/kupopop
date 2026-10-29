using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;

namespace Kupopop;

/// <summary>Fonts and icons come from the installed game/Dalamud, never remote artwork.</summary>
public sealed class UiAssets : IDisposable
{
    // Verified in local Companion and ClassJob sheets, not redistributed assets.
    public const uint Moogle = 4472;
    public const uint DeliveryMoogle = 4498;
    public const uint Pictomancer = 62142;
    private readonly IFontHandle title = Plugin.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Jupiter23));
    private readonly DiagnosticLog log;
    private readonly HashSet<uint> unavailable = [];
    public UiAssets(DiagnosticLog log) => this.log = log;

    public void Heading(string text)
    {
        using var font = title.Push();
        ImGui.TextUnformatted(text);
    }

    public void Icon(uint id, float size)
    {
        try
        {
            var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(id, hiRes: true)).GetWrapOrDefault();
            if (texture is not null) { ImGui.Image(texture.Handle, UiTheme.V(size, size)); return; }
        }
        catch (Exception ex) { if (unavailable.Add(id)) log.Write($"ui.game_icon_unavailable icon={id}", ex); }
        var top = ImGui.GetCursorScreenPos();
        ImGui.Dummy(UiTheme.V(size, size));
        var draw = ImGui.GetWindowDrawList();
        draw.AddCircle(top + UiTheme.V(size / 2, size / 2), UiTheme.S(size * 0.4f), UiTheme.C(UiTheme.Line));
        draw.AddText(top + UiTheme.V(size * 0.38f, size * 0.28f), UiTheme.C(UiTheme.Brass), "K");
    }

    public static void Glyph(FontAwesomeIcon icon, Vector4? color = null)
    {
        using var font = Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push();
        ImGui.TextColored(color ?? UiTheme.Muted, icon.ToIconString());
    }

    public void Dispose() => title.Dispose();
}
