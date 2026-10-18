using System.Text;
using BountyBay.Core.Protocol;

namespace BountyBay.Core.Data;

public sealed class ActionTable
{
    private readonly List<string> _codes;
    private readonly Dictionary<string, string> _animations;

    private ActionTable(List<string> codes, Dictionary<string, string> animations)
    {
        _codes = codes;
        _animations = animations;
    }

    public int Count => _codes.Count;

    public int Attack
    {
        get
        {
            int index = _codes.IndexOf(ProtocolConstants.AttackActionCode);
            return index >= 0 ? index : ProtocolConstants.AttackActionFallback;
        }
    }

    public int IndexOf(string code) => _codes.IndexOf(code);

    // Gesture names from Action.ini (wave_1, salute, pray_1) are the set-0 animation names of actgroup.ini without the
    // set number: wave_1 -> wave0_1 (216), salute -> salute0 (220).
    public int Gesture(string name)
    {
        int index = IndexOf(name);
        if (index >= 0) return index;
        int split = name.LastIndexOf('_');
        string animation = split > 0 && int.TryParse(name[(split + 1)..], out _) ? $"{name[..split]}0{name[split..]}" : name + "0";
        return _animations.TryGetValue(animation, out string? code) ? IndexOf(code) : -1;
    }

    public static ActionTable Load(string resourcePath)
    {
        string path = Path.Combine(resourcePath, "ini", "actgroup.ini");
        var codes = new Dictionary<int, string>();
        var animations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(path))
        {
            string section = "";
            foreach (string raw in File.ReadLines(path, Encoding.Latin1))
            {
                string line = raw.Trim();
                if (line.StartsWith('['))
                {
                    section = line.Trim('[', ']').ToLowerInvariant();
                }
                else if (line.Contains('='))
                {
                    string[] parts = line.Split('=', 2);
                    if (section == "prior" && int.TryParse(parts[0].Trim(), out int position) && position >= 0)
                        codes[position] = parts[1].Trim();
                    else if (section == "0")
                        animations.TryAdd(parts[1].Trim(), parts[0].Trim());
                }
            }
        }
        var list = new List<string>();
        if (codes.Count > 0)
        {
            for (int i = 0; i <= codes.Keys.Max(); i++)
                list.Add(codes.GetValueOrDefault(i, ""));
        }
        return new ActionTable(list, animations);
    }
}
