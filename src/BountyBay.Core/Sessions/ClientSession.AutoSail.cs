using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const string AutoSailProperty = "AutoDrive";

    public bool AutoSailOn => !Character.AutoSailOff;

    public void ToggleAutoSail()
    {
        Character.AutoSailOff = !Character.AutoSailOff;
        Save();
        SystemMessage(AutoSailOn
            ? "Auto Navigate is on. If your ship has dropped anchor, clicking on the sea raises the anchor and sets sail."
            : "You deactivated the Auto Navigate. Use the \"Cast anchor\" and \"Set sail\" commands to control the ship.");
        Log.Debug(Tag, $"{Name} turns Auto Navigate {(AutoSailOn ? "on" : "off")}");
    }

    public void AutoSailOnMove()
    {
        if (!AtSea || SeaSunk || !AutoSailOn || Properties.Int(AutoSailProperty, 0) != 0) return;
        SetProperty(AutoSailProperty, 1, visibleToOthers: true);
    }
}
