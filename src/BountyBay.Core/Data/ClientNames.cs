using BountyBay.Core.Configuration;

namespace BountyBay.Core.Data;

// Display names from the client's namelist.idres ([SKILL_SWORD] name=Sword-playing, [US_SWORD1] name=Stab, ...).
public static class ClientNames
{
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _names;

    public static string Of(EmulatorSettings settings, string key)
    {
        lock (Gate)
        {
            if (_names == null)
            {
                _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string path = Path.Combine(settings.ResourcePath, "UI", "lang", "china", "mapped", "namelist.idres");
                try
                {
                    if (File.Exists(path))
                    {
                        foreach (IniSection section in IniFile.Load(path).Sections)
                        {
                            if (section.Name.Length > 0 && section["name"] is { } name)
                                _names.TryAdd(section.Name, name.Replace('\u0003', ' ').Trim());
                        }
                    }
                }
                catch (IOException)
                {
                }
            }
            return _names.TryGetValue(key, out string? found) && found.Length > 0 ? found : key;
        }
    }
}
