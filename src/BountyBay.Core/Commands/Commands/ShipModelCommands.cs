using BountyBay.Core.Logging;
using BountyBay.Core.Protocol.Packets;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class ShipModelCommands : ITextCommand
{
    public IEnumerable<string> Names =>
        ["csMixModel", "csChooseShipModel", "csShipModelMergeClose", "CHANGE_SHIP_MODEL", "csShipInner", "RebuildYN", "KeepValueYN", "EnsureUnRebuild"];

    public bool Execute(ClientSession session, string[] words)
    {
        int number = words.Length >= 2 && int.TryParse(words[1], out int value) ? value : -1;
        switch (words[0])
        {
            case "csMixModel":
                session.MixShipModels();
                return true;
            case "csChooseShipModel":
                session.ChooseShipModel(number < 0 ? ShipModelPackets.NoChoice : number);
                return true;
            case "csShipModelMergeClose":
                session.CloseShipModelMerge();
                return true;
            case "CHANGE_SHIP_MODEL":
                session.ChangeShipModel();
                return true;
            case "csShipInner":
                session.ShowShipModelInner();
                return true;
            case "RebuildYN" when number > 0:
                session.InstallShipModelInner(number);
                return true;
            case "KeepValueYN" when number > 0:
                session.KeepShipModelInner(number);
                return true;
            case "EnsureUnRebuild" when number > 0:
                session.SplitShipModelInner(number);
                return true;
            default:
                Log.Debug(session.Tag, $"{session.Name} ship model notice: {string.Join(' ', words)}");
                return true;
        }
    }
}
