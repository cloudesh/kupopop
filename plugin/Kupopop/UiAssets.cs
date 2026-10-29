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
    private readonly DiagnosticLog log;
    private bool mascotUnavailable;
    private bool mascotReady;
    private static bool paperUnavailable;
    public UiAssets(DiagnosticLog log) => this.log = log;

    public void Heading(string text)
    {
        using var font = title.Push();
        ImGui.TextUnformatted(text);
    }

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

    public static bool PaperTexture(Vector2 min, Vector2 max)
    {
        if (paperUnavailable) return false;
        try
        {
            var texture = Plugin.TextureProvider.GetFromGame("ui/uld/MoogleColectionPaper_hr1.tex").GetWrapOrDefault();
            if (texture is null) return false;
            var draw = ImGui.GetWindowDrawList();
            uint tint = UiTheme.C(new Vector4(1, 1, 1, 0.25f));
            float tile = UiTheme.S(96);
            // Tile only the grain inside the game's paper sheet, excluding its rim.
            for (float y = min.Y; y < max.Y; y += tile)
                for (float x = min.X; x < max.X; x += tile)
                {
                    var end = Vector2.Min(new Vector2(x + tile, y + tile), max);
                    var uvEnd = new Vector2(0.18f) + (end - new Vector2(x, y)) / tile * 0.64f;
                    draw.AddImage(texture.Handle, new Vector2(x, y), end, new Vector2(0.18f), uvEnd, tint);
                }
            return true;
        }
        catch (Exception ex) { paperUnavailable = true; Plugin.Log.Debug(ex, "Kupopop paper texture unavailable; using native grain."); return false; }
    }

    public static void Glyph(FontAwesomeIcon icon, Vector4? color = null)
    {
        using var font = Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push();
        ImGui.TextColored(color ?? UiTheme.Muted, icon.ToIconString());
    }

    public void Dispose() => title.Dispose();
}
