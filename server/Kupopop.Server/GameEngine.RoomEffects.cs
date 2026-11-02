using System.Security.Cryptography;
using Kupopop.Shared;

namespace Kupopop.Server;

public sealed partial class GameEngine
{
    private static readonly (RoomEffect Effect, int Weight)[] TurnEffects =
    [
        (RoomEffect.MirrorInk, 20), (RoomEffect.BorrowedCrayons, 30),
        (RoomEffect.DoublePoints, 30), (RoomEffect.StickerStarter, 20)
    ];
    private static readonly byte[][] CrayonSets = [[29, 6, 10], [17, 11, 7], [25, 13, 19], [1, 4, 23]];

    private void StartEffect(Room room)
    {
        // Older drawers predict their strokes locally and cannot apply these rules.
        if (room.Drawer is not { } id || !peers[id].SupportsRoomEffects)
        {
            return;
        }
        var enabled = TurnEffects.Where(entry => room.Options.Effects.HasFlag(entry.Effect)).ToArray();
        int Roll(int limit) => effectRoll?.Invoke(limit) ?? RandomNumberGenerator.GetInt32(limit);
        if (enabled.Length == 0 || Roll(100) >= 20)
        {
            return;
        }
        int ticket = Roll(enabled.Sum(entry => entry.Weight));
        foreach (var entry in enabled)
        {
            if (ticket < entry.Weight)
            {
                room.Effect = entry.Effect;
                break;
            }
            ticket -= entry.Weight;
        }

        if (room.Effect == RoomEffect.BorrowedCrayons)
        {
            room.Crayons = CrayonSets[Roll(CrayonSets.Length)];
        }
        else if (room.Effect == RoomEffect.StickerStarter)
        {
            // Ordinary drawing events also render on older spectator clients.
            DrawOperation[] marks = Roll(3) switch
            {
                0 => [new("ellipse", 148, 164, 176, 192, 14, 3)],
                1 => [new("line", 160, 182, 170, 164, 14, 3), new("line", 170, 164, 180, 182, 14, 3),
                      new("line", 180, 182, 190, 164, 14, 3)],
                _ => [new("line", 170, 178, 177, 188, 14, 3), new("line", 177, 188, 192, 191, 14, 3),
                      new("line", 192, 191, 203, 181, 14, 3)]
            };
            foreach (var mark in marks)
            {
                ApplyAndBroadcastStroke(room, mark, null);
            }
        }
        SystemMessage(room, $"Room effect: {RoomEffects.Name(room.Effect)}!");
    }

    private static int AwardPoints(Room room, Member member, int basePoints)
    {
        int bonus = room.Effect == RoomEffect.DoublePoints ? basePoints : 0;
        if (bonus > 0)
        {
            room.BonusPoints[member.Id] = room.BonusPoints.GetValueOrDefault(member.Id) + bonus;
        }
        member.Score += basePoints + bonus;
        return basePoints + bonus;
    }

    private bool CanTossPom(Room room, Guid id) =>
        room.Phase == "intermission" && room.EncoreUntil > Now && !room.PomSenders.Contains(id) &&
        room.Solvers.Any(solver => solver.PlayerId == id);

    private void TossPom(IGamePeer peer, Room? room, Guid turn)
    {
        if (room is null || room.TurnId != turn || !CanTossPom(room, peer.Id))
        {
            return;
        }
        room.PomSenders.Add(peer.Id);
        Broadcast(room, new("encore-pom", ActorId: peer.Id, TurnId: turn, PomCount: room.PomSenders.Count));
    }
}
