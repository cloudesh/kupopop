using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kupopop.Shared;

namespace Kupopop;

public static class UiPalette
{
    private static readonly string[] Names = ["White", "Ink", "Graphite", "Silver", "Rose", "Scarlet", "Amber", "Sunshine", "Moss", "Forest", "Aqua", "Sky", "Violet", "Plum", "Clay", "Peach", "Transparent",
        "Wine", "Brick", "Apricot", "Sand", "Ochre", "Olive", "Mint", "Seafoam", "Teal", "Steel blue", "Ice", "Pearl", "Indigo", "Lavender", "Lilac", "Blush"];
    private static Vector4 Colour(byte index)
    {
        uint c = Palette.Colors[index];
        return new((c & 255) / 255f, ((c >> 8) & 255) / 255f, ((c >> 16) & 255) / 255f, (c >> 24) / 255f);
    }
    public static void Button(ref byte selected, bool transparency)
    {
        if (ImGui.ColorButton("Color", Colour(selected), ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, UiTheme.V(28, 28)))
            ImGui.OpenPopup("colour-palette");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Color: " + Names[selected] + ". Click to open the palette.");
        if (selected == Palette.Transparent) TransparentGlyph();
        if (ImGui.BeginPopup("colour-palette"))
        {
            Grid(ref selected, transparency);
            ImGui.EndPopup();
        }
    }
    public static void Grid(ref byte selected, bool transparency)
    {
        UiTheme.Caption("32 COLORS");
        for (int i = 0; i < Palette.Opaque.Length; i++)
        {
            byte index = Palette.Opaque[i];
            if (i % 8 != 0) ImGui.SameLine(0, UiTheme.S(4));
            if (ImGui.ColorButton(Names[index], Colour(index), ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, UiTheme.V(26, 26)))
            { selected = index; ImGui.CloseCurrentPopup(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Names[index]);
            if (selected == index) ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin() - Vector2.One, ImGui.GetItemRectMax() + Vector2.One, UiTheme.C(UiTheme.Rose), 0, 0, UiTheme.S(2));
        }
        if (transparency)
        {
            if (ImGui.ColorButton("Transparent", Colour(Palette.Transparent), ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, UiTheme.V(26, 26)))
            { selected = Palette.Transparent; ImGui.CloseCurrentPopup(); }
            TransparentGlyph();
            ImGui.SameLine(); UiTheme.Caption("Transparent / erase background");
        }
    }
    private static void TransparentGlyph()
    {
        var min = ImGui.GetItemRectMin() + Vector2.One;
        var max = ImGui.GetItemRectMax() - Vector2.One;
        var half = (min + max) / 2;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, UiTheme.C(UiTheme.Panel));
        draw.AddRectFilled(min, half, UiTheme.C(UiTheme.Line));
        draw.AddRectFilled(half, max, UiTheme.C(UiTheme.Line));
        draw.AddLine(new(min.X + 3, max.Y - 3), new(max.X - 3, min.Y + 3), UiTheme.C(UiTheme.Rose), UiTheme.S(2));
    }
}
