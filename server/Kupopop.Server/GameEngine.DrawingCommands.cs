using Kupopop.Shared;

namespace Kupopop.Server;

public sealed partial class GameEngine
{
    private void DrawOnCanvas(IGamePeer peer, Room? room, ClientCommand command)
    {
        if (room?.Phase != "drawing" || room.Drawer != peer.Id || command.TurnId != room.TurnId)
        {
            throw new ArgumentException("Drawing is only available during your current turn.");
        }

        if (Now >= room.Deadline)
        {
            EndTurn(room);
            return;
        }
        if (command.Draw is null || !room.Canvas.Valid(command.Draw))
        {
            throw new ArgumentException("Drawing operation rejected.");
        }

        room.History.Capture(room.Canvas, command.Draw.StrokeId);
        room.Canvas.Apply(command.Draw);
        Broadcast(room, new("draw", Draw: command.Draw, ActorId: peer.Id, TurnId: room.TurnId));
    }

    private void ChooseWord(IGamePeer peer, Room? room, ClientCommand command)
    {
        if (room?.Phase != "choosing" || room.Drawer != peer.Id || command.TurnId != room.TurnId ||
            Now >= room.Deadline || command.Text is null || !room.Choices.Contains(command.Text, StringComparer.Ordinal))
        {
            throw new ArgumentException("Choose one of your offered words before the timer ends.");
        }

        BeginDrawing(room, command.Text);
    }

    private void ChooseCustomWord(IGamePeer peer, Room? room, ClientCommand command)
    {
        if (room?.Phase != "choosing" || room.Drawer != peer.Id || command.TurnId != room.TurnId ||
            Now >= room.Deadline || !room.Options.CustomWordsEnabled || !room.CustomWordAvailable)
        {
            throw new ArgumentException("A custom word is not available for this turn.");
        }

        string custom = command.Text?.Trim() ?? "";
        if (!GuessMatching.ValidCustomWord(custom))
        {
            throw new ArgumentException("Use 2–48 letters, numbers, spaces, apostrophes or hyphens, with at least two letters.");
        }

        BeginDrawing(room, custom, custom: true);
    }

    private void MoveCanvasCursor(IGamePeer peer, Room? room, ClientCommand command)
    {
        if (room?.Phase != "drawing" || room.Drawer != peer.Id || command.TurnId != room.TurnId || Now >= room.Deadline)
        {
            return; // A cursor already in flight may arrive after the turn changes.
        }

        var cursor = command.Cursor;
        if (cursor is null || cursor.X is < 0 or >= Protocol.CanvasWidth || cursor.Y is < 0 or >= Protocol.CanvasHeight ||
            !DrawingTools.Valid(cursor.Tool) || !Palette.Valid(cursor.Color, false) || cursor.Size is < 1 or > 20)
        {
            throw new ArgumentException("Canvas cursor rejected.");
        }

        room.Cursor = cursor;
        room.CursorAt = Now;
        Broadcast(room, new("cursor", Cursor: cursor, ActorId: peer.Id, TurnId: room.TurnId));
    }

    private void UndoDrawing(IGamePeer peer, Room? room, ClientCommand command)
    {
        if (room?.Phase != "drawing" || room.Drawer != peer.Id || command.TurnId != room.TurnId || Now >= room.Deadline)
        {
            throw new ArgumentException("Undo is only available during your current turn.");
        }

        if (!room.History.Undo(room.Canvas))
        {
            throw new ArgumentException("Nothing to undo.");
        }

        Broadcast(room, new("canvas", Canvas: (byte[])room.Canvas.Pixels.Clone(), TurnId: room.TurnId, CanUndo: room.History.CanUndo));
    }
}
