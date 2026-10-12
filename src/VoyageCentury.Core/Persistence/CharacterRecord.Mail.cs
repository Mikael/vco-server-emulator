namespace VoyageCentury.Core.Persistence;

public sealed class LetterEntry
{
    public int Id { get; set; }
    public string Sender { get; set; } = "";
    public int PaperType { get; set; }
    public string Date { get; set; } = "";
    public string Content { get; set; } = "";
    public bool Saved { get; set; }
    public bool Delivered { get; set; }
}

public sealed class ParcelEntry
{
    public string Sender { get; set; } = "";
    public string Date { get; set; } = "";
    public string Message { get; set; } = "";
    public int View { get; set; } = 3;
    public ItemStack Item { get; set; } = new();
}

public sealed partial class CharacterRecord
{
    public int NextLetterId { get; set; } = 1;
    public List<LetterEntry> Letters { get; set; } = [];
    public List<ParcelEntry> Parcels { get; set; } = [];
}
