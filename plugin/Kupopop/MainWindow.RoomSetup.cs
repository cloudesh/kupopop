using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Kupopop.Shared;

namespace Kupopop;

public sealed partial class MainWindow
{
    private void CreateRoomScreen()
    {
        bool editing = room is not null;
        if (PageHeading(editing ? "Room settings" : "Host Room"))
        {
            return;
        }

        var tabs = ImGui.GetCursorScreenPos();
        float tabHeight = UiTheme.S(34);
        ImGui.GetWindowDrawList().AddLine(tabs + new Vector2(0, tabHeight), tabs + new Vector2(ImGui.GetContentRegionAvail().X, tabHeight), UiTheme.C(UiTheme.Line));
        if (UiTheme.PanelTab("Basic", roomSetupPage == 0))
        {
            roomSetupPage = 0;
        }

        ImGui.SameLine(0, UiTheme.S(3));
        if (UiTheme.PanelTab("Advanced", roomSetupPage == 1))
        {
            roomSetupPage = 1;
        }

        ImGui.SameLine(0, UiTheme.S(3));
        if (UiTheme.PanelTab("Word Lists", roomSetupPage == 2))
        {
            roomSetupPage = 2;
        }

        ImGui.SameLine(0, UiTheme.S(3));
        if (UiTheme.PanelTab("Custom Words", roomSetupPage == 3))
        {
            roomSetupPage = 3;
        }

        ImGui.SetCursorScreenPos(tabs + new Vector2(0, tabHeight));
        using var card = new UiTheme.PaperPanel("room-settings", Vector2.Zero, scrollable: false, flush: true, attachedTabs: true);
        if (!card.Success)
        {
            return;
        }

        using (ImRaii.PushColor(ImGuiCol.ChildBg, Vector4.Zero))
        using (var body = ImRaii.Child("room-settings-page", new Vector2(0, -UiTheme.S(46)), false))
        {
            if (body.Success)
            {
                if (roomSetupPage == 1)
                {
                    UiRoomSettings.Advanced(ref hints, ref wordChoiceCount, ref voteKickEnabled, ref voteSkipEnabled, ref customWordsEnabled);
                }
                else if (roomSetupPage == 2)
                {
                    WordListPage();
                }
                else if (roomSetupPage == 3)
                {
                    CustomWordsPage();
                }
                else
                {
                    UiTheme.Field("Room name", "##room-name", ref roomTitle, 40, "Name your drawing room");
                    UiTheme.Gap(5);
                    UiRoomSettings.Basic(ref seconds, ref rounds, ref capacity);
                    UiTheme.Gap(12);
                    ImGui.Checkbox("Public lobby", ref publicRoom);
                    if (!editing)
                    {
                        ImGui.Checkbox("Invite friends after creating", ref inviteAfterCreate);
                    }

                    UiTheme.Wrapped(publicRoom
                        ? "Anyone can see the room name and player count. Your profile is visible after they join."
                        : "Private room. Only people with your code can join.", UiTheme.Muted);
                }
            }
        }
        UiTheme.Gap(4);
        bool validCustom = TryCustomWords(out int customCount);
        using (ImRaii.Disabled(savingRoomSettings || client?.Connected != true || !Protocol.ValidName(roomTitle) || !validCustom || selectedWordLists.Count == 0 && customCount < wordChoiceCount))
        {
            if (UiTheme.Button(editing ? "Save settings" : "Host Room", UiTheme.S(170), primary: true))
            {
                bool sent = Send(new(editing ? "configure" : "create", Options: new(roomTitle, publicRoom, Math.Clamp(seconds, 15, 300), Math.Clamp(rounds, 1, 5),
                    Math.Clamp(hints, 0, 300), Math.Clamp(capacity, 2, 16), selectedWordLists.ToArray(),
                    Math.Clamp(wordChoiceCount, 1, 8), voteKickEnabled, voteSkipEnabled, customWordsEnabled), CustomWords: customWordsText));
                savingRoomSettings = editing && sent;
            }
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        UiTheme.Caption($"{selectedWordLists.Count} word lists selected");
    }

    private void OpenRoomSetup(bool publicByDefault = false)
    {
        roomSetupPage = 0;
        customWordsText = "";
        publicRoom = publicByDefault;
        screen = "create";
    }

    private void EditRoomSettings()
    {
        if (room is null)
        {
            return;
        }

        var options = room.Options;
        roomTitle = options.Title;
        publicRoom = options.IsPublic;
        seconds = options.TurnSeconds;
        rounds = options.Rounds;
        hints = options.HintSeconds;
        capacity = options.MaxPlayers;
        wordChoiceCount = options.WordChoiceCount;
        voteKickEnabled = options.VoteKickEnabled;
        voteSkipEnabled = options.VoteSkipEnabled;
        customWordsEnabled = options.CustomWordsEnabled;
        customWordsText = roomCustomWords;
        selectedWordLists.Clear();
        foreach (var id in options.WordLists ?? [])
        {
            selectedWordLists.Add(id);
        }

        wordListsChosen = true;
        roomSetupPage = 0;
        screen = "create";
    }

    private void WordListPage()
    {
        if (UiTheme.Button("Normal lists", 0, quiet: true))
        {
            selectedWordLists.Clear();
            foreach (var list in wordLists.Where(l => l.DefaultEnabled))
            {
                selectedWordLists.Add(list.Id);
            }

            wordListsChosen = true;
        }
        ImGui.SameLine();
        if (UiTheme.Button("All", 0, quiet: true))
        {
            foreach (var list in wordLists)
            {
                selectedWordLists.Add(list.Id);
            }

            wordListsChosen = true;
        }
        ImGui.SameLine();
        if (UiTheme.Button("None", 0, quiet: true))
        {
            selectedWordLists.Clear();
            wordListsChosen = true;
        }
        UiTheme.Gap(5);
        foreach (var group in wordLists.GroupBy(l => l.Category))
        {
            using var categoryId = ImRaii.PushId(group.Key);
            var members = group.ToArray();
            int selected = members.Count(l => selectedWordLists.Contains(l.Id));
            bool included = selected > 0;
            if (ImGui.Checkbox("##category-enabled", ref included))
            {
                bool hasNormal = members.Any(l => l.DefaultEnabled);
                foreach (var list in members)
                {
                    if (included && (list.DefaultEnabled || !hasNormal))
                    {
                        selectedWordLists.Add(list.Id);
                    }
                    else
                    {
                        selectedWordLists.Remove(list.Id);
                    }
                }
                wordListsChosen = true;
            }
            if (ImGui.IsItemHovered())
            {
                UiTheme.Tooltip("Include or exclude this category. Enabling it selects its normal lists; advanced lists stay optional.");
            }

            ImGui.SameLine();
            bool collapsed = collapsedWordCategories.Contains(group.Key);
            if (UiTheme.CategoryHeader(group.Key, $"{selected}/{members.Length} lists · {members.Sum(l => l.Count):N0} words", !collapsed))
            {
                if (collapsed)
                {
                    collapsedWordCategories.Remove(group.Key);
                }
                else
                {
                    collapsedWordCategories.Add(group.Key);
                }
            }
            if (collapsed)
            {
                continue;
            }

            int columns = ImGui.GetContentRegionAvail().X >= UiTheme.S(760) ? 3 : 2;
            if (!ImGui.BeginTable("word-list-buttons", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoPadOuterX))
            {
                continue;
            }

            try
            {
                foreach (var list in members)
                {
                    ImGui.TableNextColumn();
                    bool enabled = selectedWordLists.Contains(list.Id);
                    string detail = $"{list.Count} words" + (list.Complex ? " - Advanced" : "");
                    if (UiTheme.ListButton(list.Id, list.Name, detail, enabled))
                    {
                        if (enabled)
                        {
                            selectedWordLists.Remove(list.Id);
                        }
                        else
                        {
                            selectedWordLists.Add(list.Id);
                        }

                        wordListsChosen = true;
                    }
                    if (ImGui.IsItemHovered())
                    {
                        UiTheme.Tooltip(list.Name + "\n" + detail + (list.Complex ? "\nDisabled by default." : ""));
                    }
                }
            }
            finally { ImGui.EndTable(); }
            UiTheme.Gap(4);
        }
    }
}
