namespace Kupopop.Server;

public sealed partial class GameEngine
{
    private void LikeDrawing(IGamePeer peer, Room? room, Guid turn)
    {
        if (room?.Phase != "drawing" || room.TurnId != turn || Now >= room.Deadline ||
            room.Drawer is not { } drawer || drawer == peer.Id || !room.Members[peer.Id].Connected)
            throw new ArgumentException("You can like another artist's current drawing.");
        // Kept for the entire turn, including disconnect/rejoin. Never trust a
        // client count or store which player liked a drawing in the database.
        if (room.DrawingLikes.Contains(peer.Id)) return;
        if (room.HostWords.Length == 0) accounts.RecordDrawingLike(drawer);
        room.DrawingLikes.Add(peer.Id);
        BroadcastState(room);
    }
}
