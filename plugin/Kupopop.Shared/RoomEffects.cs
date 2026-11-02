namespace Kupopop.Shared;

[Flags]
public enum RoomEffect
{
    None = 0, MirrorInk = 1, BorrowedCrayons = 2, DoublePoints = 4, StickerStarter = 8, Encore = 16,
    All = MirrorInk | BorrowedCrayons | DoublePoints | StickerStarter | Encore
}

public static class RoomEffects
{
    public const string CapabilityHeader = "X-Kupopop-Room-Effects";
    public static string Name(RoomEffect effect) => effect switch
    {
        RoomEffect.MirrorInk => "Mirror Ink",
        RoomEffect.BorrowedCrayons => "Borrowed Crayons",
        RoomEffect.DoublePoints => "Double Points",
        RoomEffect.StickerStarter => "Sticker Starter",
        RoomEffect.Encore => "Encore",
        _ => ""
    };

    public static DrawOperation Reflect(DrawOperation stroke) => stroke with
    {
        X1 = Protocol.CanvasWidth - 1 - stroke.X1,
        X2 = Protocol.CanvasWidth - 1 - stroke.X2
    };
}
