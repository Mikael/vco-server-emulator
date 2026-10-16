using System.Globalization;
using BountyBay.Core.Sessions;

namespace BountyBay.Core.Commands.Commands;

public sealed class SeaBombCommands : ITextCommand
{
    public IEnumerable<string> Names => ["csPutDownBomb"];

    public bool Execute(ClientSession session, string[] words)
    {
        if (words.Length < 3 || !float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            || !float.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return false;
        session.PutDownSeaBomb(x, z);
        return true;
    }
}
