namespace VoyageCentury.Core.Persistence;

public interface IAccountBackend
{
    Dictionary<string, AccountRecord> Load();

    void Save(Dictionary<string, AccountRecord> accounts);
}
