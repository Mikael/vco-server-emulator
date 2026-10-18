using System.Numerics;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const string HurricaneExplorerKey = "gnnpc0118";
    private const string HurricaneCaptainKey = "npc51001";
    private const string HurricaneOrderItem = "DJ007216";
    public const int HurricaneBeach = 1601002;
    private const int HurricaneGoOption = 47101;
    private const int HurricaneBackOption = 47102;
    private static readonly Vector3 HurricaneArrival = new(2.5f, 2.36f, -30.5f);

    private bool TalkHurricane(NpcDefinition npc)
    {
        if (npc.NameKey == HurricaneExplorerKey)
        {
            int left = HurricaneTripsLeft;
            Send(ChatPackets.NpcDialogue(npc.Id,
            [
                (DialogueEntry.Text, 0, left > 0
                    ? "Hurricane Island lies beyond the reach of any chart. Pirates, barbarians and stranger things live there. I can take you to its beach once a day."
                    : "You have already been to Hurricane Island today. A Hurricane Island Order lets you go once more."),
                (DialogueEntry.Option, HurricaneGoOption, "Go to Hurricane Island Beach"),
                (DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"),
            ]), "(hurricane explorer)");
            return true;
        }
        if (npc.NameKey == HurricaneCaptainKey)
        {
            Send(ChatPackets.NpcDialogue(npc.Id,
            [
                (DialogueEntry.Text, 0, "My ship sails back to the harbour whenever you are ready."),
                (DialogueEntry.Option, HurricaneBackOption, $"Sail back to {HurricaneHomeName}"),
                (DialogueEntry.Option, (int)DialogueAction.Bye, "Goodbye"),
            ]), "(hurricane captain)");
            return true;
        }
        return false;
    }

    private bool HandleHurricaneOption(int target, int option)
    {
        if (option == HurricaneGoOption)
        {
            CloseNpcDialogue();
            GoToHurricane();
            return true;
        }
        if (option == HurricaneBackOption)
        {
            CloseNpcDialogue();
            LeaveHurricane();
            return true;
        }
        return false;
    }

    private int HurricaneTripsLeft
    {
        get
        {
            ResetHurricaneDay();
            return Math.Max(0, 1 + Character.HurricaneExtraTrips - Character.HurricaneTrips);
        }
    }

    private void ResetHurricaneDay()
    {
        int today = Rankings.Today;
        if (Character.HurricaneDay == today) return;
        Character.HurricaneDay = today;
        Character.HurricaneTrips = 0;
        Character.HurricaneExtraTrips = 0;
    }

    private string HurricaneHomeName
        => Context.Data.Scenes.Exists(Character.HurricaneReturnScene) && Character.HurricaneReturnScene != 0
            ? Context.Data.Npcs.Values.FirstOrDefault(npc => npc.Scene == Character.HurricaneReturnScene)?.SceneName ?? "the harbour"
            : "Jamestown Harbor";

    private void GoToHurricane()
    {
        if (AtSea || Mover == null || SceneLocked()) return;
        if (HurricaneTripsLeft <= 0)
        {
            SystemMessage("You have already been to Hurricane Island today. Use a Hurricane Island Order to go once more.", SystemMessageKind.Prompt);
            return;
        }
        if (!Context.Data.Scenes.Exists(HurricaneBeach))
        {
            SystemMessage("Hurricane Island cannot be reached right now.");
            return;
        }
        (float x, float z) = Mover.Now();
        Character.HurricaneReturnScene = Scene;
        Character.HurricaneReturnPosition = [x, Height, z];
        Character.HurricaneTrips++;
        Save();
        Log.Info(Tag, $"{Name} goes to Hurricane Island ({Character.HurricaneTrips} trip(s) today)");
        _linkSerial++;
        GoTo(HurricaneBeach, HurricaneArrival.X, HurricaneArrival.Y, HurricaneArrival.Z);
        SystemMessage("You arrive on Hurricane Island Beach. Captain Migna can take you back.");
    }

    public void LeaveHurricane()
    {
        int scene = Character.HurricaneReturnScene;
        float[]? at = Character.HurricaneReturnPosition;
        if (scene == 0 || !Context.Data.Scenes.Exists(scene) || at is not { Length: 3 })
        {
            NpcDefinition? explorer = Context.Data.Npcs.Values.FirstOrDefault(npc => npc.NameKey == HurricaneExplorerKey);
            if (explorer == null) return;
            scene = explorer.Scene;
            at = [explorer.X + 1.5f, explorer.Y, explorer.Z + 1.5f];
        }
        Log.Info(Tag, $"{Name} leaves Hurricane Island for scene {scene}");
        _linkSerial++;
        GoTo(scene, at[0], at[1], at[2]);
    }

    private bool UseHurricaneOrder(int index, Persistence.ItemStack item)
    {
        if (item.Name != HurricaneOrderItem) return false;
        ResetHurricaneDay();
        if (Character.HurricaneTrips < 1 + Character.HurricaneExtraTrips)
        {
            SystemMessage("You can still go to Hurricane Island today without an Order.", SystemMessageKind.Prompt);
            return true;
        }
        Character.HurricaneExtraTrips++;
        TakeFromBag(index, 1, "(hurricane island order used)");
        Save();
        SystemMessage("You may enter Hurricane Island once more today.");
        return true;
    }
}
