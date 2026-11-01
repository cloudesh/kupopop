using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private string suggestionKind = "suggestion", suggestionCategory = "FFXIV", suggestionText = "";
    private bool suggestNewCategory;
    private string proposedCategory = "";
    private long suggestionSentAt;

    private void SuggestionsScreen()
    {
        float rightEdge = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        if (UiTheme.PanelTab("Suggestion", suggestionKind == "suggestion")) { suggestionKind = "suggestion"; suggestionText = ""; }
        ImGui.SameLine(0, UiTheme.S(3));
        if (UiTheme.PanelTab("Suggest a word", suggestionKind == "word")) { suggestionKind = "word"; suggestionText = ""; }
        ImGui.SameLine();
        string backLabel = room is null ? "Back to home" : "Back to room";
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), rightEdge - ImGui.CalcTextSize(backLabel).X - UiTheme.S(36)));
        if (BackButton()) return;
        using var panel = new UiTheme.PaperPanel("suggestion-page", Vector2.Zero, attachedTabs: true);
        if (!panel.Success) return;
        bool word = suggestionKind == "word";
        if (word)
        {
            ImGui.SetNextItemWidth(UiTheme.S(220));
            if (ImGui.BeginCombo("Category", suggestNewCategory ? "New category..." : suggestionCategory))
            {
                foreach (string choice in wordLists.Select(l => l.Category).Distinct())
                    if (ImGui.Selectable(choice, !suggestNewCategory && choice == suggestionCategory))
                    { suggestionCategory = choice; suggestNewCategory = false; }
                ImGui.Separator();
                if (ImGui.Selectable("New category...", suggestNewCategory)) suggestNewCategory = true;
                ImGui.EndCombo();
            }
            if (suggestNewCategory)
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                ImGui.InputTextWithHint("##new-word-category", "New category name", ref proposedCategory, 41);
            }
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputTextWithHint("##suggested-word", "A word or short phrase", ref suggestionText, 49);
        }
        else
        {
            bool empty = suggestionText.Length == 0;
            ImGui.InputTextMultiline("##suggestion-text", ref suggestionText, 1001,
                new Vector2(ImGui.GetContentRegionAvail().X, Math.Min(UiTheme.S(220), Math.Max(UiTheme.S(80), ImGui.GetContentRegionAvail().Y - UiTheme.S(100)))));
            if (empty && !ImGui.IsItemActive())
                ImGui.GetWindowDrawList().AddText(ImGui.GetItemRectMin() + ImGui.GetStyle().FramePadding,
                    UiTheme.C(UiTheme.Muted), "Tell us what would make your drawing room better.");
        }
        UiTheme.Wrapped("Sent privately to the maintainer for review. Please leave personal details out. Up to five submissions a day, one per minute. Words are not added automatically.", UiTheme.Muted);
        string category = suggestNewCategory ? proposedCategory.Trim() : suggestionCategory;
        bool valid = word ? CustomWordLists.ValidWord(suggestionText.Trim()) && Protocol.ValidName(category) : suggestionText.Trim().Length is >= 5 and <= 1000;
        using (ImRaii.Disabled(!valid || client?.Connected != true || Environment.TickCount64 - suggestionSentAt < 1000))
            if (UiTheme.Button("Send suggestion", UiTheme.S(170), primary: true) &&
                Send(new("suggest", Text: suggestionText, Kind: suggestionKind, Category: word ? category : null)))
                suggestionSentAt = Environment.TickCount64;
    }
}
