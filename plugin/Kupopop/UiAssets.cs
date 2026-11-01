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
    private readonly IFontHandle title = Plugin.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Jupiter23));
    private readonly IFontHandle large = Plugin.PluginInterface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis36));
    private readonly DiagnosticLog log;
    private bool mascotUnavailable;
    private bool mascotReady;
    private static bool paperUnavailable;
    private static bool paperReady;
    private static DiagnosticLog? paperLog;
    public UiAssets(DiagnosticLog log) { this.log = log; paperLog = log; }

    public void Heading(string text)
    {
        using var font = title.Push();
        ImGui.TextUnformatted(text);
    }

    public void LargeText(string text, bool wrap = false)
    {
        using var font = large.Push();
        if (wrap) ImGui.TextWrapped(text); else ImGui.TextUnformatted(text);
    }
    public void DrawLarge(string text, Vector2 position, Vector4 color)
    {
        using var font = large.Push();
        ImGui.GetWindowDrawList().AddText(position, UiTheme.C(color), text);
    }
    public Vector2 MeasureLarge(string text, float width)
    {
        using var font = large.Push();
        return ImGui.CalcTextSize(text, false, width);
    }
    public void Hint(string hint) { using var font = title.Push(); UiPresentation.Hint(hint); }
    public void CenteredHeading(string text, float width)
    { using var font = title.Push(); UiPresentation.CenteredLabel(text, width, UiTheme.Ink); }

    public void Mascot(float size)
    {
        if (!mascotUnavailable)
        {
            try
            {
                // Painted Mogpendium artwork with transparent edges, not a minion
                // item tile. This texture remains in the user's game installation.
                var texture = Plugin.TextureProvider.GetFromGame("ui/uld/MoogleCollectionTitle_hr1.tex").GetWrapOrDefault();
                if (texture is not null)
                {
                    if (!mascotReady) { mascotReady = true; log.Write($"ui.mascot_ready size={texture.Width}x{texture.Height}"); }
                    ImGui.Image(texture.Handle, UiTheme.V(size, size)); return;
                }
            }
            catch (Exception ex) { mascotUnavailable = true; log.Write("ui.mascot_unavailable", ex); }
        }
        ImGui.Dummy(UiTheme.V(size, size));
    }

    public static bool PaperTexture(Vector2 min, Vector2 max, Vector4? tint = null)
    {
        if (paperUnavailable) return false;
        try
        {
            var shared = Plugin.TextureProvider.GetFromGame("ui/uld/MoogleColectionlWindow_hr1.tex");
            if (!shared.TryGetWrap(out var texture, out var error))
            {
                if (error is not null) { paperUnavailable = true; paperLog?.Write("ui.paper_unavailable", error); }
                return false;
            }
            if (!paperReady) { paperReady = true; paperLog?.Write($"ui.paper_ready size={texture.Width}x{texture.Height}"); }
            // Use one continuous, flat interior below the game's divider.
            // Repeating a shaded patch produced visible horizontal color bands.
            UiStationery.PaperMaterial(texture.Handle, min, max, tint);
            return true;
        }
        catch (Exception ex) { paperUnavailable = true; paperLog?.Write("ui.paper_unavailable", ex); return false; }
    }

    public static void Glyph(FontAwesomeIcon icon, Vector4? color = null)
    {
        using var font = Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push();
        ImGui.TextColored(color ?? UiTheme.Muted, icon.ToIconString());
    }

    public void Dispose() { title.Dispose(); large.Dispose(); }
}
