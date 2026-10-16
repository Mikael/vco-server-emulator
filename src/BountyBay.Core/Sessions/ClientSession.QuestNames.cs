using System.Text.RegularExpressions;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private static readonly string[] QuestLeadWords = ["at", "least", "the", "each", "of", "all", "a", "an", "some", "more", "every"];

    private static bool SameQuestName(string wanted, string name)
        => wanted.Equals(name, StringComparison.OrdinalIgnoreCase) || QuestNameKey(wanted) == QuestNameKey(name);

    private static string QuestNameKey(string text)
    {
        string lower = Regex.Replace(text ?? "", @"[\\]?L?AskGoToObj\s*\S+\s+[^#]*#", " ").ToLowerInvariant();
        lower = Regex.Split(lower, @"、|,|;|\band\b|\bor\b|\bkill\b|和|\bthen\b")[0];
        lower = Regex.Replace(lower, @"\b\d+\b", " ");
        lower = Regex.Replace(lower, @"[^a-z0-9' -]", " ");
        List<string> words = Regex.Split(lower, @"[\s'-]+").Where(word => word.Length > 0).ToList();
        while (words.Count > 0 && QuestLeadWords.Contains(words[0])) words.RemoveAt(0);
        return string.Concat(words.Select(SingularWord));
    }

    private static string SingularWord(string word) => word switch
    {
        "men" => "man", "mummies" => "mummy", "wolves" => "wolf", "shamen" => "shaman", "snowmen" => "snowman",
        "cavemen" => "caveman", "axmen" or "axemen" => "axeman", "leaves" => "leaf",
        _ when word.EndsWith("men") && word.Length > 4 => word[..^3] + "man",
        _ when word.EndsWith("ies") && word.Length > 4 => word[..^3] + "y",
        _ when word.EndsWith("ves") && word.Length > 4 => word[..^3] + "f",
        _ when Regex.IsMatch(word, "(ches|shes|xes|sses)$") => word[..^2],
        _ when word.EndsWith('s') && !Regex.IsMatch(word, "(ss|us|is)$") && word.Length > 3 => word[..^1],
        _ => word,
    };
}
