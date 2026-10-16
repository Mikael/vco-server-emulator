using BountyBay.Core.Protocol;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    public string UnionViewed { get; set; } = "";

    public int UnionPage { get; set; }

    public void ApplyColonyProperties()
    {
        if (PropertyTable.Has("UnionName")) Properties["UnionName"] = Character.UnionName;
    }

    public void ApplyUnion()
    {
        if (!PropertyTable.Has("UnionName")) return;
        string code = Character.UnionName;
        if (Properties.TryGetValue("UnionName", out object? current) && Equals(current, code)) return;
        SetProperty("UnionName", code, visibleToOthers: true);
    }
}
