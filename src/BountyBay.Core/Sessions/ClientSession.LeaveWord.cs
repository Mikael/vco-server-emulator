using BountyBay.Core.Logging;
using BountyBay.Core.Persistence;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int LeaveWordFirstFindingTable = 60;
    private const int LeaveWordLastFindingTable = 64;

    private static readonly string[] LeaveWordFindingKinds = ["animal", "conch", "nature", "plant", "relic"];

    public void OfferLeaveWordInvite(int paperType)
    {
        if (!Character.LeaveWordInvites.Contains(paperType)) Character.LeaveWordInvites.Add(paperType);
        Save();
        Send(ChatPackets.Script(0, $"scInviteStyle {paperType}"), "(invitation letter)");
    }

    public void SendLeaveWordInvite(int paperType, string recipient, string content)
    {
        if (!Character.LeaveWordInvites.Contains(paperType))
        {
            SystemMessage("You have no invitation of this kind to send.", SystemMessageKind.Prompt);
            return;
        }
        string[] names = recipient.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length == 0 || names.Length > MostRecipients)
        {
            SystemMessage($"An invitation can go to at most {MostRecipients} names, separated by semicolons without spaces.");
            return;
        }
        var targets = new List<CharacterRecord>();
        foreach (string name in names)
        {
            CharacterRecord? target = MailDirectory.Find(Context, name);
            if (target == null)
            {
                SystemMessage($"Nobody called {name} is known here.");
                return;
            }
            if (target.Blacklist.Contains(Name) || target.Letters.Count >= MostLetters)
            {
                SystemMessage($"{target.Name} cannot receive your invitation.");
                return;
            }
            targets.Add(target);
        }
        if (content.Length > LetterPaper.MaxLength) content = content[..LetterPaper.MaxLength];
        foreach (CharacterRecord target in targets)
        {
            target.Letters.Add(new LetterEntry
            {
                Id = target.NextLetterId++,
                Sender = Name,
                PaperType = paperType,
                Date = MailDate(),
                Content = content,
            });
        }
        Character.LeaveWordInvites.Remove(paperType);
        Context.Accounts.Save();
        SystemMessage($"Your invitation to {string.Join(", ", targets.Select(target => target.Name))} has been sent.");
        foreach (CharacterRecord target in targets) Context.World.FindPlayer(target.Name)?.AnnounceLetters();
        Log.Info(Tag, $"{Name} sends invitation paper {paperType} to {string.Join(", ", targets.Select(target => target.Name))}");
    }

    public void WatchFindings(int table)
    {
        if (table < LeaveWordFirstFindingTable || table > LeaveWordLastFindingTable) return;
        string kind = LeaveWordFindingKinds[table - LeaveWordFirstFindingTable];
        SystemMessage($"You have not recorded any {kind} finding yet.", SystemMessageKind.Prompt);
        Log.Debug(Tag, $"{Name} opens the {kind} findings (table {table})");
    }
}
