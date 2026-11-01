using System.Numerics;
using Dalamud.Bindings.ImGui;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private bool TryCustomWords(out int count)
    {
        try { count = CustomWordLists.Parse(customWordsText).Length; return true; }
        catch (ArgumentException) { count = 0; return false; }
    }

    private void CustomWordsPage()
    {
        UiTheme.Caption("YOUR ROOM'S WORDS");
        UiTheme.Wrapped("Separate words with commas. Add them to the selected lists, or select None under Word Lists to use only your own.", UiTheme.Muted);
        ImGui.InputTextMultiline("##custom-room-words", ref customWordsText, CustomWordLists.MaximumTextLength + 1,
            new Vector2(ImGui.GetContentRegionAvail().X, Math.Max(UiTheme.S(90), ImGui.GetContentRegionAvail().Y - UiTheme.S(95))));
        if (TryCustomWords(out int count)) UiTheme.Caption($"{count} / {CustomWordLists.MaximumWords} words · 2–48 characters each");
        else UiTheme.Wrapped("Check the list: up to 64 words, 2–48 characters each. Use letters, numbers, spaces, apostrophes or hyphens.", UiTheme.Rose);
        UiTheme.Wrapped("These words stay in this room and are not added to the public catalog. Matches using them keep room scores but do not add lifetime rankings or likes.", UiTheme.Muted);
    }
}
