using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private string suggestionKind = "suggestion", suggestionCategory = "FFXIV", suggestionText = "";
    private long suggestionSentAt;

    private void SuggestionsScreen()
    {
        if (PageHeading("Leave a little inspiration")) return;
        if (UiTheme.PanelTab("Suggestion", suggestionKind == "suggestion")) { suggestionKind = "suggestion"; suggestionText = ""; }
        ImGui.SameLine(0, UiTheme.S(3));
        if (UiTheme.PanelTab("Suggest a word", suggestionKind == "word")) { suggestionKind = "word"; suggestionText = ""; }
        using var panel = new UiTheme.PaperPanel("suggestion-page", Vector2.Zero, attachedTabs: true);
        if (!panel.Success) return;
        bool word = suggestionKind == "word";
        UiTheme.Wrapped(word ? "Have a word that would be fun to draw? Send it for review." : "Tell us what would make your drawing room better.");
        UiTheme.Gap(8);
        if (word)
        {
            ImGui.SetNextItemWidth(UiTheme.S(220));
            if (ImGui.BeginCombo("Category", suggestionCategory))
            {
                foreach (string category in wordLists.Select(l => l.Category).Distinct())
                    if (ImGui.Selectable(category, category == suggestionCategory)) suggestionCategory = category;
                ImGui.EndCombo();
            }
            UiTheme.Field("Your word", "##suggested-word", ref suggestionText, 48, "A word or short phrase");
        }
        else ImGui.InputTextMultiline("##suggestion-text", ref suggestionText, 1001,
            new Vector2(ImGui.GetContentRegionAvail().X, Math.Min(UiTheme.S(220), Math.Max(UiTheme.S(80), ImGui.GetContentRegionAvail().Y - UiTheme.S(100)))));
        UiTheme.Wrapped("Sent privately to the maintainer for review. Please leave personal details out. Up to five submissions a day, one per minute. Words are not added automatically.", UiTheme.Muted);
        bool valid = word ? CustomWordLists.ValidWord(suggestionText.Trim()) : suggestionText.Trim().Length is >= 5 and <= 1000;
        using (ImRaii.Disabled(!valid || client?.Connected != true || Environment.TickCount64 - suggestionSentAt < 1000))
            if (UiTheme.Button("Send suggestion", UiTheme.S(170), primary: true) &&
                Send(new("suggest", Text: suggestionText, Kind: suggestionKind, Category: word ? suggestionCategory : null)))
                suggestionSentAt = Environment.TickCount64;
    }
}
