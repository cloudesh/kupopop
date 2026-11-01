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
    private static bool paperUnavailable;
    private static bool paperReady;
    private static DiagnosticLog? paperLog;
    public UiAssets(DiagnosticLog log) { paperLog = log; }

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
    public void Hint(string hint, float width = 0) { using var font = title.Push(); UiPresentation.Hint(hint, width); }
    public Vector2 MeasureWord(string? word, string hint, float width)
    {
        using var font = title.Push();
        return word is null ? UiPresentation.MeasureHint(hint, width) : ImGui.CalcTextSize(word, false, width);
    }
    public void WordHeading(string word, float width)
    {
        using var font = title.Push();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width); ImGui.TextWrapped(word); ImGui.PopTextWrapPos();
    }
    public void CenteredHeading(string text, float width)
    { using var font = title.Push(); UiPresentation.CenteredLabel(text, width, UiTheme.Ink); }

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
