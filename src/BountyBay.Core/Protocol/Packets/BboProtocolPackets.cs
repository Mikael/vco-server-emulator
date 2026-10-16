using System.Globalization;

namespace BountyBay.Core.Protocol.Packets;

public sealed record BboInviteCard(string Name, int Profession, int ProfessionLevel, int LandPoints, int SeaPoints, string Position,
    string Guild, string Text);

public sealed record BboGuildDetail(bool ApplyClosed, int Flag, string Tenet, string Name, int Members, int MemberLimit, int Level,
    int Fund, int ReserveFund, string Headquarters, int Business, int Industry, int Agriculture, int Military, int TotalVigor,
    int DailyVigor);

public sealed record BboOceanNpc(int Id, byte Kind, int Model, string Name, float CollideRadius, int Hp, int MaxHp, int SailHp,
    int MaxSailHp, int Sailors, int MaxSailors, bool Aggressive);

public static class BboProtocolPackets
{
    public const char Field = UiPackets.FieldSeparator;
    public const int OnlineAwardReady = 1;
    public const int FirstOnlineStage = 1;

    public static byte[] ShowShortWay() => UiPackets.Custom("ShowShortWay");

    public static byte[] HideShortWay() => UiPackets.Custom("HideShortWay");

    public static byte[] TeamInviteCard(BboInviteCard card) => UiPackets.Custom("scShowQuest " + Card(card));

    public static byte[] TeamJoinCard(BboInviteCard card) => UiPackets.Custom("scShowJoin " + Card(card));

    public static byte[] TeamMemberInfo(string text) => UiPackets.Custom("scGetTeamMembInfo " + UiPackets.Token(Cell(text)));

    public static byte[] TeamAdded() => UiPackets.Custom("scSuccAddTeam");

    public static byte[] TeamVote(int seconds, string command, IReadOnlyList<string> options, string title)
        => UiPackets.Custom(string.Join(' ', new[] { seconds.ToString(CultureInfo.InvariantCulture), UiPackets.Token(Cell(command)), Math.Min(options.Count, 4).ToString(CultureInfo.InvariantCulture) }
            .Concat(options.Take(4).Select(option => UiPackets.Token(Cell(option))))
            .Append(UiPackets.Token(Cell(title))).Prepend("scTeamVote")));

    public static byte[] TeamDamage(string name, int first, int second, int third)
        => UiPackets.Custom($"TeamMemDamage 0 {UiPackets.Token(Cell(name))} {Math.Max(0, first)} {Math.Max(0, second)} {Math.Max(0, third)}");

    public static byte[] ConsDetail(BboGuildDetail detail)
        => UiPackets.Custom("ConsDetail " + string.Join(Field, new[]
        {
            detail.ApplyClosed ? "1" : "0", detail.Flag.ToString(CultureInfo.InvariantCulture), Cell(detail.Tenet), Cell(detail.Name),
            Number(detail.Members), Number(detail.MemberLimit), Number(detail.Level), Number(detail.Fund),
            Number(detail.ReserveFund / 10000), Cell(detail.Headquarters), Number(detail.Business), Number(detail.Industry),
            Number(detail.Agriculture), Number(detail.Military), Number(detail.TotalVigor), Number(detail.DailyVigor),
        }));

    public static byte[] ConsLog(int total, string text) => UiPackets.Custom($"scConsLogInfo {Math.Max(0, total)} {UiPackets.Token(Cell(text))}");

    public static byte[] AwardCountdown(int stage, int seconds) => UiPackets.Custom($"scAwardStatus 0 {stage} {Math.Max(0, seconds)}");

    public static byte[] AwardReady(int stage) => UiPackets.Custom($"scAwardStatus 1 {OnlineAwardReady} {stage}");

    public static byte[] VipInfo(int level, string text, int value, int seconds)
        => UiPackets.Custom($"scShowVIPInfo {Math.Clamp(level, 1, 7)} {UiPackets.Token(Cell(text))} {Math.Max(0, value)} {Math.Max(0, seconds)}");

    public static byte[] RebuildShipShow() => UiPackets.Custom("ShowRebuildShip");

    public static byte[] RebuildShipEffect(IEnumerable<(string Label, int Delta)> effects)
        => UiPackets.Custom(string.Join(' ', effects.Take(9).Select(effect => $"{UiPackets.Token(Cell(effect.Label))} {effect.Delta}").Prepend("RebuildShipEffect")));

    public static byte[] RebuildShipResult(bool close, string message)
        => UiPackets.Custom($"RebuildShipResult {(close ? 1 : 0)} {UiPackets.Token(Cell(message))}");

    public static byte[] ShipCoreShow() => UiPackets.Custom("ShowRebuildShipCore");

    public static byte[] InnerRebuildShow() => UiPackets.Custom("ShowRebuldView");

    public static byte[] ShipMergeReady() => UiPackets.Custom("scCanShipMerge 0");

    public static byte[] ShipMergeHide() => UiPackets.Custom("scHideShipMerge");

    public static IEnumerable<byte[]> OceanNpc(BboOceanNpc npc, float x, float z)
    {
        yield return ScenePackets.CreateObject(npc.Id, (ObjectKind)npc.Kind, npc.Model);
        var properties = new PropertyBag
        {
            ["Name"] = npc.Name,
            ["Model"] = npc.Model,
            ["CollideRadius"] = npc.CollideRadius,
        };
        if (npc.Kind is BboSeaPackets.KindOceanNpc or BboSeaPackets.KindOceanNpcB or BboSeaPackets.KindOceanNpcC)
        {
            properties["S/HP"] = npc.Hp;
            properties["S/MaxHP"] = npc.MaxHp;
            properties["S/SailHP"] = npc.SailHp;
            properties["S/MaxSailHP"] = npc.MaxSailHp;
            properties["NumSailor"] = npc.Sailors;
            properties["NumSailorMax"] = npc.MaxSailors;
        }
        if (npc.Kind == BboSeaPackets.KindOceanNpc) properties["FirstAttack"] = npc.Aggressive ? 1 : 0;
        yield return ScenePackets.ObjectProperties(npc.Id, SeaPackets.Known(properties));
        yield return ScenePackets.PlaceObject(npc.Id, x, 0f, z);
        yield return ScenePackets.ShowObject(npc.Id);
    }

    public static string Cell(string? text)
    {
        string cleaned = (text ?? "").Replace("\r", "").Replace('\n', ' ').Replace(Field, ' ').Trim();
        return cleaned.Length == 0 ? "-" : cleaned;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Card(BboInviteCard card) => string.Join(Field, new[]
    {
        Cell(card.Name), card.Profession is >= 1 and <= 5 ? card.Profession.ToString(CultureInfo.InvariantCulture) : "0",
        Number(card.ProfessionLevel), Number(card.LandPoints), Number(card.SeaPoints), Cell(card.Position), Cell(card.Guild), Cell(card.Text),
    });
}
