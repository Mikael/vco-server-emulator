using BountyBay.Core.Logging;

namespace BountyBay.Core.Persistence;

public sealed class AccountRepository
{
    public const string Unclaimed = "_unclaimed";

    private readonly IAccountBackend _backend;
    private readonly Dictionary<string, AccountRecord> _accounts;
    private readonly object _gate = new();

    public AccountRepository(IAccountBackend backend)
    {
        _backend = backend;
        _accounts = backend.Load();
    }

    public int AccountCount
    {
        get { lock (_gate) return _accounts.Count; }
    }

    public List<CharacterRecord> Characters(string account)
    {
        lock (_gate)
        {
            if (!_accounts.TryGetValue(account, out AccountRecord? record))
            {
                if (_accounts.Remove(Unclaimed, out AccountRecord? unclaimed)) record = unclaimed;
                else record = new AccountRecord();
                _accounts[account] = record;
                SaveLocked();
            }
            return record.Characters;
        }
    }

    public bool CheckPassword(string account, string passwordHash)
    {
        lock (_gate)
        {
            if (!_accounts.TryGetValue(account, out AccountRecord? record))
            {
                record = new AccountRecord();
                _accounts[account] = record;
            }
            if (record.PasswordHash.Length == 0)
            {
                record.PasswordHash = passwordHash;
                SaveLocked();
                return true;
            }
            return record.PasswordHash == passwordHash;
        }
    }

    public CharacterRecord? FindCharacter(string name)
    {
        lock (_gate)
        {
            return _accounts.Values.SelectMany(account => account.Characters)
                .FirstOrDefault(character => character.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Save()
    {
        lock (_gate) SaveLocked();
    }

    private void SaveLocked()
    {
        try
        {
            _backend.Save(_accounts);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("Accounts", $"Save failed: {exception.Message}");
        }
    }

    public List<CharacterRecord> AllCharacters()
    {
        lock (_gate) return _accounts.Values.SelectMany(account => account.Characters).ToList();
    }
}
