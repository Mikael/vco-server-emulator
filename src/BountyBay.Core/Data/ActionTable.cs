using System.Text;
using BountyBay.Core.Protocol;

namespace BountyBay.Core.Data;

public sealed class ActionTable
{
    private readonly List<string> _codes;

    private ActionTable(List<string> codes) => _codes = codes;

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

    public static ActionTable Load(string resourcePath)
    {
        string path = Path.Combine(resourcePath, "ini", "actgroup.ini");
        var codes = new Dictionary<int, string>();
        if (File.Exists(path))
        {
            bool inside = false;
            foreach (string raw in File.ReadLines(path, Encoding.Latin1))
            {
                string line = raw.Trim();
                if (line.StartsWith('['))
                {
                    inside = line.Equals("[prior]", StringComparison.OrdinalIgnoreCase);
                }
                else if (inside && line.Contains('='))
                {
                    string[] parts = line.Split('=', 2);
                    if (int.TryParse(parts[0].Trim(), out int position) && position >= 0)
                        codes[position] = parts[1].Trim();
                }
            }
        }
        var list = new List<string>();
        if (codes.Count > 0)
        {
            for (int i = 0; i <= codes.Keys.Max(); i++)
                list.Add(codes.GetValueOrDefault(i, ""));
        }
        return new ActionTable(list);
    }
}
