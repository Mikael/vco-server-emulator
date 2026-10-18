using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BountyBay.Core.Configuration;
using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Network;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public static class GuildWarPhase
{
    public const int Bidding = 0;
    public const int Preparing = 1;
    public const int Battle = 2;
}

public static class GuildWarState
{
    public const int Neutral = 0;
    public const int Battle = 1;
}

public sealed class GuildWarBid
{
    public string Guild { get; set; } = "";
    public string Leader { get; set; } = "";
    public long Units { get; set; }
}

public sealed class GuildWarEntry
{
    public string Name { get; set; } = "";
    public string Guild { get; set; } = "";
    public int State { get; set; } = GuildWarRegistration.Awaiting;
}

public sealed class GuildWarFighter
{
    public string Name { get; set; } = "";
    public string Guild { get; set; } = "";
    public bool Attacker { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Merit { get; set; }
    public bool Present { get; set; }
    public int ReturnScene { get; set; }
}

public sealed class GuildWarCity
{
    public string Name { get; set; } = "";
    public string Owner { get; set; } = "";
    public DateTime? OwnedSince { get; set; }
    public GuildWarSchedule? Schedule { get; set; }
    public int Phase { get; set; }
    public DateTime NextStart { get; set; }
    public string Attacker { get; set; } = "";
    public long AttackerUnits { get; set; }
    public List<GuildWarBid> Bids { get; set; } = [];
    public List<GuildWarEntry> Entries { get; set; } = [];
    public List<string> Committed { get; set; } = [];
    public string StatueMessage { get; set; } = "";

    [JsonIgnore]
    public Dictionary<string, GuildWarFighter> Fighters { get; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTime BattleStart(GuildWarSchedule schedule) => NextStart.AddMinutes(schedule.Prepare + schedule.Recon);

    public DateTime BattleEnd(GuildWarSchedule schedule) => BattleStart(schedule).AddMinutes(schedule.Battle);

    public bool Involves(string guild)
        => Attacker.Length > 0 && (GuildDirectory.Same(Attacker, guild) || GuildDirectory.Same(Owner, guild));
}

public sealed class GuildWarStanding
{
    public string Guild { get; set; } = "";
    public int State { get; set; }
    public string Week { get; set; } = "";
    public int EntriesUsed { get; set; }
}

public sealed class GuildWarStore
{
    public List<GuildWarCity> Cities { get; set; } = [];
    public List<GuildWarStanding> Guilds { get; set; } = [];
}

public static class GuildWarDirectory
{
    private static GuildWarStore? _store;
    private static string _path = "";
    private static long _lastTick;

    public static GuildWarRules Rules(ServerContext context) => GuildWarData.For(context.Settings).War;

    public static GuildWarSchedule Schedule(ServerContext context, GuildWarCity city) => city.Schedule ?? Rules(context).Schedule;

    public static GuildWarCity? City(ServerContext context, string name)
        => Store(context).Cities.FirstOrDefault(city => GuildDirectory.Same(city.Name, name));

    public static GuildWarCity? CityHere(ClientSession session)
        => City(session.Context, GuildWarData.CityOf(session.Context.Settings, session.Scene));

    public static IEnumerable<GuildWarCity> Owned(ServerContext context, string guild)
        => Store(context).Cities.Where(city => city.Owner.Length > 0 && GuildDirectory.Same(city.Owner, guild));

    public static GuildWarCity? WarOf(ServerContext context, string guild)
        => Store(context).Cities.Where(city => city.Phase != GuildWarPhase.Bidding && city.Involves(guild)).OrderBy(city => city.NextStart).FirstOrDefault()
        ?? Store(context).Cities.Where(city => city.Bids.Any(bid => GuildDirectory.Same(bid.Guild, guild))).FirstOrDefault()
        ?? Owned(context, guild).FirstOrDefault();

    public static GuildWarCity? BattleOf(ClientSession session)
        => Store(session.Context).Cities.FirstOrDefault(city => city.Phase == GuildWarPhase.Battle && city.Fighters.TryGetValue(session.Name, out GuildWarFighter? fighter)
            && fighter.Present && session.Scene == Rules(session.Context).WarScene);

    public static void Tick(ServerContext context)
    {
        long now = Environment.TickCount64;
        if (now - _lastTick < 1000) return;
        _lastTick = now;
        ImpeachDirectory.Tick();
        DateTime clock = DateTime.Now;
        foreach (GuildWarCity city in Store(context).Cities)
        {
            GuildWarSchedule schedule = Schedule(context, city);
            if (city.Phase == GuildWarPhase.Bidding && clock >= city.NextStart)
            {
                if (!schedule.Open) city.NextStart = NextStart(schedule, clock);
                else CloseBidding(context, city);
                Save();
            }
            else if (city.Phase == GuildWarPhase.Preparing && clock >= city.BattleStart(schedule))
            {
                StartBattle(context, city);
                Save();
            }
            else if (city.Phase == GuildWarPhase.Battle && clock >= city.BattleEnd(schedule))
            {
                Finish(context, city, null);
                Save();
            }
        }
    }

    public static void ShowConsWar(ClientSession session)
    {
        Tick(session.Context);
        Guild? guild = session.Guild;
        if (guild == null)
        {
            session.SystemMessage("You are not in a guild.");
            return;
        }
        GuildWarRules rules = Rules(session.Context);
        GuildWarStanding standing = Standing(session.Context, guild.Name);
        GuildWarCity? war = WarOf(session.Context, guild.Name);
        int approved = war?.Entries.Count(entry => GuildDirectory.Same(entry.Guild, guild.Name) && entry.State == GuildWarRegistration.Approved) ?? 0;
        var bars = new List<(int, int)>();
        for (int index = 0; index < 9; index++)
        {
            int max = index < rules.BarMax.Count ? rules.BarMax[index] : 100;
            bars.Add(index == 5 ? (approved, Math.Max(rules.MaxRegistrations, 1)) : (0, max));
        }
        session.Send(GuildWarPackets.ConsWar(standing.State, bars), "(guild war state)");
    }

    public static void ChangeState(ClientSession session, int state)
    {
        if (!Leader(session, GuildDuty.ViceLeader, "Only the leader and vice leaders decide the guild's war stance.", out Guild guild)) return;
        if (state is not (GuildWarState.Neutral or GuildWarState.Battle)) return;
        GuildWarStanding standing = Standing(session.Context, guild.Name);
        if (state == GuildWarState.Neutral && Store(session.Context).Cities.Any(city => city.Involves(guild.Name) || city.Bids.Any(bid => GuildDirectory.Same(bid.Guild, guild.Name))))
        {
            session.SystemMessage("The guild is involved in a siege war and cannot declare neutrality now.");
        }
        else if (standing.State != state)
        {
            standing.State = state;
            Save();
            Tell(session.Context, guild, state == GuildWarState.Battle ? "The guild is now ready for battle." : "The guild now keeps neutrality.");
        }
        ShowConsWar(session);
    }

    public static void ShowBids(ClientSession session, GuildWarCity city)
    {
        Tick(session.Context);
        GuildWarRules rules = Rules(session.Context);
        session.GuildWarCityName = city.Name;
        long highest = city.Bids.Count > 0 ? city.Bids.Max(bid => bid.Units) : 0;
        long minimum = Math.Max(rules.MinBidUnits, highest + rules.MinRaiseUnits);
        bool canBid = BidObstacle(session, city).Length == 0;
        session.Send(GuildWarPackets.ShowBidList(canBid, highest, minimum), $"(siege bids for {city.Name})");
        session.Send(GuildPackets.Clear(GuildWarList.Bidders), "(clear siege bidders)");
        foreach (byte[] row in GuildPackets.Rows(GuildWarList.Bidders,
            city.Bids.OrderByDescending(bid => bid.Units).Select(bid => new[] { bid.Guild, bid.Leader, bid.Units.ToString(CultureInfo.InvariantCulture) })))
            session.Send(row, "(siege bidders)");
    }

    public static void BeforeBid(ClientSession session)
    {
        GuildWarCity? city = SessionCity(session);
        if (city == null) return;
        string obstacle = BidObstacle(session, city);
        if (obstacle.Length > 0)
        {
            session.SystemMessage(obstacle);
            return;
        }
        GuildWarRules rules = Rules(session.Context);
        Guild guild = session.Guild!;
        long highest = city.Bids.Count > 0 ? city.Bids.Max(bid => bid.Units) : 0;
        long own = city.Bids.FirstOrDefault(bid => GuildDirectory.Same(bid.Guild, guild.Name))?.Units ?? 0;
        long raise = Math.Max(1, Math.Max(rules.MinBidUnits, highest + rules.MinRaiseUnits) - own);
        long affordable = guild.Fund / Math.Max(1, rules.BidUnit);
        session.Send(GuildWarPackets.BidInput((int)Math.Min(raise, int.MaxValue), (int)Math.Min(Math.Min(affordable, rules.MaxBidUnits - own), int.MaxValue),
            $"Bid for the siege of {city.Name} in units of {rules.BidUnit} silver coins. Your guild has bid {own}; the highest bid is {highest}. Enter at least {raise} more.",
            "Run for the territory supervisory right", "Increase"), "(siege bid input)");
    }

    public static void Bid(ClientSession session, long units)
    {
        GuildWarCity? city = SessionCity(session);
        if (city == null) return;
        string obstacle = BidObstacle(session, city);
        if (obstacle.Length > 0)
        {
            session.SystemMessage(obstacle);
            return;
        }
        GuildWarRules rules = Rules(session.Context);
        Guild guild = session.Guild!;
        GuildWarBid? bid = city.Bids.FirstOrDefault(entry => GuildDirectory.Same(entry.Guild, guild.Name));
        long own = bid?.Units ?? 0;
        long highest = city.Bids.Where(entry => entry != bid).Select(entry => entry.Units).DefaultIfEmpty(0).Max();
        long total = own + units;
        long minimum = Math.Max(rules.MinBidUnits, highest + rules.MinRaiseUnits);
        if (units <= 0 || total < minimum)
        {
            session.SystemMessage($"Your guild's bid must reach at least {minimum} ({minimum * rules.BidUnit} silver coins).");
            return;
        }
        if (total > rules.MaxBidUnits)
        {
            session.SystemMessage($"A bid cannot exceed {rules.MaxBidUnits} ({(long)rules.MaxBidUnits * rules.BidUnit} silver coins).");
            return;
        }
        long cost = units * rules.BidUnit;
        if (cost > guild.Fund)
        {
            session.SystemMessage($"The guild fund holds only {guild.Fund} silver coins; this bid costs {cost}.");
            return;
        }
        guild.Fund -= (int)cost;
        if (bid == null) city.Bids.Add(bid = new GuildWarBid { Guild = guild.Name });
        bid.Units = total;
        bid.Leader = session.Name;
        GuildDirectory.Save();
        Save();
        GuildDirectory.Refresh(session);
        Tell(session.Context, guild, $"{session.Name} raised the guild's bid for the siege of {city.Name} to {total * rules.BidUnit} silver coins.");
        Log.Info(session.Tag, $"{session.Name} bids {total} units for {city.Name} for {guild.Name}");
        foreach (ClientSession viewer in session.Context.World.Sessions.Where(other => other.InWorld && other.GuildWarCityName == city.Name))
            ShowBids(viewer, city);
    }

    public static void ShowRegistrations(ClientSession session)
    {
        Tick(session.Context);
        session.Send(GuildPackets.Clear(GuildWarList.Registrations), "(clear siege registrations)");
        Guild? guild = session.Guild;
        if (guild == null) return;
        GuildWarCity? city = WarOf(session.Context, guild.Name);
        if (city == null) return;
        foreach (byte[] row in GuildPackets.Rows(GuildWarList.Registrations, city.Entries.Where(entry => GuildDirectory.Same(entry.Guild, guild.Name))
            .Select(entry => new[] { entry.Name, Online(session.Context, entry.Name) != null ? "1" : "0", entry.State.ToString() })))
            session.Send(row, "(siege registrations)");
    }

    public static void Apply(ClientSession session)
    {
        Guild? guild = session.Guild;
        GuildMember? self = guild?.Member(session.Name);
        if (guild == null || self == null)
        {
            session.SystemMessage("You are not in a guild.");
            return;
        }
        GuildWarCity? city = WarOf(session.Context, guild.Name);
        if (city == null || (!city.Involves(guild.Name) && !city.Bids.Any(bid => GuildDirectory.Same(bid.Guild, guild.Name))))
        {
            session.SystemMessage("Your guild takes part in no siege war at the moment.");
            return;
        }
        if (city.Entries.Any(entry => GuildDirectory.Same(entry.Name, session.Name)))
        {
            session.SystemMessage("You have already registered for the battle.");
            return;
        }
        GuildWarRules rules = Rules(session.Context);
        if (city.Entries.Count(entry => GuildDirectory.Same(entry.Guild, guild.Name)) >= rules.MaxRegistrations)
        {
            session.SystemMessage("Your guild cannot register more members for this battle.");
            return;
        }
        int state = self.Duty >= rules.OfficerDuty ? GuildWarRegistration.Approved : GuildWarRegistration.Awaiting;
        city.Entries.Add(new GuildWarEntry { Name = session.Name, Guild = guild.Name, State = state });
        Save();
        session.SystemMessage(state == GuildWarRegistration.Approved
            ? $"You registered for the siege of {city.Name}."
            : $"You applied to fight in the siege of {city.Name}. An officer must approve the request.");
        PublishRegistrations(session.Context, guild);
    }

    public static void Decide(ClientSession session, string name, bool accept)
    {
        if (!Leader(session, Rules(session.Context).OfficerDuty, "Only guild directors and leaders decide on battle requests.", out Guild guild)) return;
        GuildWarCity? city = WarOf(session.Context, guild.Name);
        GuildWarEntry? entry = city?.Entries.FirstOrDefault(other => GuildDirectory.Same(other.Name, name) && GuildDirectory.Same(other.Guild, guild.Name));
        if (city == null || entry == null)
        {
            session.SystemMessage("You have not selected a member who requested to fight.");
            ShowRegistrations(session);
            return;
        }
        if (accept) entry.State = GuildWarRegistration.Approved;
        else city.Entries.Remove(entry);
        Save();
        Online(session.Context, entry.Name)?.SystemMessage(accept
            ? $"Your request to fight in the siege of {city.Name} was approved."
            : $"Your request to fight in the siege of {city.Name} was declined.");
        PublishRegistrations(session.Context, guild);
        if (accept && city.Phase == GuildWarPhase.Battle && city.Committed.Any(other => GuildDirectory.Same(other, guild.Name))
            && Online(session.Context, entry.Name) is { } fighter)
            Enter(fighter, city);
    }

    public static void Commit(ClientSession session)
    {
        GuildWarRules rules = Rules(session.Context);
        if (!Leader(session, rules.OfficerDuty, "Only guild directors and leaders queue the guild for battle.", out Guild guild)) return;
        GuildWarCity? city = WarOf(session.Context, guild.Name);
        if (city == null || !city.Involves(guild.Name))
        {
            session.SystemMessage("Your guild has no siege battle to queue for. The bidding must have closed first.");
            return;
        }
        GuildWarStanding standing = Standing(session.Context, guild.Name);
        bool already = city.Committed.Any(other => GuildDirectory.Same(other, guild.Name));
        if (!already)
        {
            if (standing.EntriesUsed >= rules.WeeklyEntries)
            {
                session.SystemMessage("Your guild has used up this week's siege battles.");
                return;
            }
            standing.EntriesUsed++;
            city.Committed.Add(guild.Name);
            Save();
            Tell(session.Context, guild, $"{session.Name} queued the guild for the siege of {city.Name}.");
        }
        if (city.Phase != GuildWarPhase.Battle)
        {
            session.SystemMessage($"The battle for {city.Name} begins at {city.BattleStart(Schedule(session.Context, city)):yyyy-MM-dd HH:mm}.");
            return;
        }
        foreach (GuildWarEntry entry in city.Entries.Where(entry => GuildDirectory.Same(entry.Guild, guild.Name) && entry.State == GuildWarRegistration.Approved))
        {
            if (Online(session.Context, entry.Name) is { } member && BattleOf(member) == null) Enter(member, city);
        }
    }

    public static void Surrender(ClientSession session)
    {
        if (!Leader(session, Rules(session.Context).BidderDuty, "Only the guild leader can surrender.", out Guild guild)) return;
        GuildWarCity? city = WarOf(session.Context, guild.Name);
        if (city == null || !city.Involves(guild.Name))
        {
            session.SystemMessage("Your guild is in no siege war.");
            return;
        }
        Log.Info(session.Tag, $"{guild.Name} surrenders the siege of {city.Name}");
        Finish(session.Context, city, GuildDirectory.Same(city.Attacker, guild.Name) ? false : true);
        Save();
    }

    public static void EntryTimes(ClientSession session)
    {
        Guild? guild = session.Guild;
        GuildWarStanding? standing = guild == null ? null : Standing(session.Context, guild.Name);
        session.Send(GuildWarPackets.EntryTimes(standing?.EntriesUsed ?? 0, Rules(session.Context).WeeklyEntries), "(siege entries this week)");
    }

    public static void Leave(ClientSession session, int argument)
    {
        GuildWarCity? city = BattleOf(session);
        if (city == null)
        {
            Log.Debug(session.Tag, $"{session.Name} csLeaveConWar {argument} outside a siege");
            return;
        }
        if (argument == -1)
        {
            session.Send(GuildWarPackets.AskLeave(), "(confirm leaving the siege)");
            return;
        }
        Exit(session, city, "(siege deserted)");
    }

    public static bool KnockedOut(ClientSession victim, ClientSession? killer)
    {
        GuildWarCity? city = BattleOf(victim);
        if (city == null) return false;
        GuildWarFighter loser = city.Fighters[victim.Name];
        loser.Deaths++;
        if (killer != null && city.Fighters.TryGetValue(killer.Name, out GuildWarFighter? winner) && winner.Attacker != loser.Attacker)
        {
            winner.Kills++;
            winner.Merit += Rules(victim.Context).KillMerit;
            killer.Send(GuildWarPackets.Kill($"You defeated {victim.Name}."), "(siege kill)");
            victim.Send(GuildWarPackets.Killed($"You were defeated by {killer.Name}."), "(siege defeat)");
        }
        else
        {
            victim.Send(GuildWarPackets.Killed("You were defeated."), "(siege defeat)");
        }
        return false;
    }

    public static void ShowStatue(ClientSession session, GuildWarCity city)
    {
        Guild? owner = city.Owner.Length > 0 ? GuildDirectory.Find(session.Context, city.Owner) : null;
        GuildMember? leader = owner?.Members.FirstOrDefault(member => member.Duty == GuildDuty.Leader);
        session.GuildWarCityName = city.Name;
        bool canEdit = owner != null && leader != null && GuildDirectory.Same(leader.Name, session.Name);
        session.Send(GuildWarPackets.ShowStatue(canEdit, owner?.Name ?? "-", leader?.Name ?? "-",
            city.OwnedSince?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-", (owner?.Commerce ?? 0).ToString(CultureInfo.InvariantCulture),
            city.StatueMessage.Length > 0 ? city.StatueMessage : "-"), $"(statue of {city.Name})");
    }

    public static void SetStatueMessage(ClientSession session, string text)
    {
        GuildWarCity? city = SessionCity(session);
        if (city == null) return;
        Guild? owner = city.Owner.Length > 0 ? GuildDirectory.Find(session.Context, city.Owner) : null;
        if (owner?.Member(session.Name) is not { Duty: GuildDuty.Leader })
        {
            session.SystemMessage($"Only the leader of the guild that rules {city.Name} can engrave the statue.");
            return;
        }
        string cleaned = UiPackets.Line(text).Trim();
        int limit = Rules(session.Context).StatueMessageLength;
        city.StatueMessage = cleaned.Length > limit ? cleaned[..limit] : cleaned;
        Save();
        session.SystemMessage($"The message on the statue of {city.Name} was changed.");
    }

    public static bool IsGm(ClientSession session)
    {
        GuildWarRules rules = Rules(session.Context);
        if (session.IsGameMaster) return true;
        return rules.GmOpenToAll && session.Context.Settings.GameMasters.Count == 0 || rules.Gms.Any(name => GuildDirectory.Same(name, session.Name) || GuildDirectory.Same(name, session.Account));
    }

    public static void ShowCitySettings(ClientSession session)
    {
        if (!IsGm(session))
        {
            session.SystemMessage("Only game masters command the siege war.");
            return;
        }
        Tick(session.Context);
        session.Send(GuildPackets.Clear(GuildWarList.CitySettings), "(clear siege settings)");
        foreach (byte[] row in GuildPackets.Rows(GuildWarList.CitySettings, Store(session.Context).Cities.Select(city =>
        {
            GuildWarSchedule schedule = Schedule(session.Context, city);
            return new[]
            {
                city.Name, schedule.Open ? "1" : "0", schedule.Week.ToString(), schedule.Hour.ToString(), schedule.Minute.ToString(),
                schedule.Prepare.ToString(), schedule.Recon.ToString(), schedule.Battle.ToString(),
            };
        })))
            session.Send(row, "(siege settings)");
        session.Send(GuildWarPackets.ShowCitySettings(), "(siege settings window)");
    }

    public static void SetCity(ClientSession session, string name, int week, int hour, int minute, int prepare, int recon, int battle)
    {
        if (!IsGm(session))
        {
            session.SystemMessage("Only game masters command the siege war.");
            return;
        }
        GuildWarCity? city = City(session.Context, name);
        if (city == null)
        {
            session.SystemMessage($"{name} is not a siege city.");
            return;
        }
        if (week is < 0 or > 6 || hour is < 0 or > 23 || minute is < 0 or > 59 || prepare < 0 || recon < 0 || battle <= 0)
        {
            session.SystemMessage("Invalid siege war settings.");
            return;
        }
        city.Schedule = new GuildWarSchedule { Open = true, Week = week, Hour = hour, Minute = minute, Prepare = prepare, Recon = recon, Battle = battle };
        if (city.Phase == GuildWarPhase.Bidding) city.NextStart = NextStart(city.Schedule, DateTime.Now);
        Save();
        session.SystemMessage($"Siege war of {city.Name}: {CultureInfo.InvariantCulture.DateTimeFormat.GetDayName((DayOfWeek)week)} {hour:00}:{minute:00}, "
            + $"preparing {prepare} min, reconnoitering {recon} min, battling {battle} min. Next war {city.NextStart:yyyy-MM-dd HH:mm}.");
        Log.Info(session.Tag, $"{session.Name} sets the siege of {city.Name}: {week} {hour}:{minute} {prepare}/{recon}/{battle}");
        ShowCitySettings(session);
    }

    public static string Describe(ServerContext context, GuildWarCity city)
    {
        Tick(context);
        GuildWarRules rules = Rules(context);
        GuildWarSchedule schedule = Schedule(context, city);
        string owner = city.Owner.Length > 0 ? $"{city.Name} is ruled by the guild {city.Owner}." : $"{city.Name} is ruled by no guild.";
        return city.Phase switch
        {
            GuildWarPhase.Preparing => $"{owner} The guild {city.Attacker} won the bidding; the battle begins at {city.BattleStart(schedule):yyyy-MM-dd HH:mm}.",
            GuildWarPhase.Battle => $"{owner} The guild {city.Attacker} is besieging the city until {city.BattleEnd(schedule):HH:mm}.",
            _ => $"{owner} Bidding for the next siege closes at {city.NextStart:yyyy-MM-dd HH:mm}; {city.Bids.Count} guild(s) have bid. "
                + $"Bids are made from the guild fund in units of {rules.BidUnit} silver coins by the guild leader.",
        };
    }

    public static void GuildDisbanded(ServerContext context, string guild)
    {
        foreach (GuildWarCity city in Store(context).Cities)
        {
            if (GuildDirectory.Same(city.Owner, guild))
            {
                city.Owner = "";
                city.OwnedSince = null;
                city.StatueMessage = "";
            }
            city.Bids.RemoveAll(bid => GuildDirectory.Same(bid.Guild, guild));
            city.Entries.RemoveAll(entry => GuildDirectory.Same(entry.Guild, guild));
            if (GuildDirectory.Same(city.Attacker, guild) && city.Phase != GuildWarPhase.Bidding) Finish(context, city, false);
        }
        Store(context).Guilds.RemoveAll(standing => GuildDirectory.Same(standing.Guild, guild));
        Save();
    }

    private static string BidObstacle(ClientSession session, GuildWarCity city)
    {
        GuildWarRules rules = Rules(session.Context);
        Guild? guild = session.Guild;
        if (guild == null) return "Only guild leaders can run for the territory supervisory right.";
        if (guild.Member(session.Name)?.Duty < rules.BidderDuty) return "Only the guild leader can bid.";
        if (guild.Level < rules.MinGuildLevel) return $"Only guilds of level {rules.MinGuildLevel} or higher can bid.";
        if (rules.RequireBattleState && Standing(session.Context, guild.Name).State != GuildWarState.Battle)
            return "Your guild keeps neutrality. Choose Enter Battle in the guild's military page first.";
        if (city.Phase != GuildWarPhase.Bidding || !Schedule(session.Context, city).Open) return $"Bidding for {city.Name} is closed.";
        if (GuildDirectory.Same(city.Owner, guild.Name)) return $"Your guild already rules {city.Name}.";
        GuildWarCity? other = Store(session.Context).Cities.FirstOrDefault(entry => entry != city
            && (entry.Bids.Any(bid => GuildDirectory.Same(bid.Guild, guild.Name)) || GuildDirectory.Same(entry.Attacker, guild.Name)));
        return other != null ? $"Your guild is already running for {other.Name}." : "";
    }

    private static GuildWarCity? SessionCity(ClientSession session)
    {
        GuildWarCity? city = session.GuildWarCityName.Length > 0 ? City(session.Context, session.GuildWarCityName) : CityHere(session);
        if (city == null) session.SystemMessage("Talk to the city official of a siege city first.");
        return city;
    }

    private static void CloseBidding(ServerContext context, GuildWarCity city)
    {
        GuildWarRules rules = Rules(context);
        GuildWarBid? top = city.Bids.Where(bid => GuildDirectory.Find(context, bid.Guild) != null).OrderByDescending(bid => bid.Units).FirstOrDefault();
        foreach (GuildWarBid bid in city.Bids.Where(bid => bid != top))
        {
            if (GuildDirectory.Find(context, bid.Guild) is { } loser)
            {
                loser.Fund = (int)Math.Min(int.MaxValue, loser.Fund + bid.Units * rules.BidUnit);
                Tell(context, loser, $"Your guild lost the bidding for {city.Name}; {bid.Units * rules.BidUnit} silver coins returned to the guild fund.");
            }
        }
        city.Bids.Clear();
        city.Entries.RemoveAll(entry => top == null || !(GuildDirectory.Same(entry.Guild, top.Guild) || GuildDirectory.Same(entry.Guild, city.Owner)));
        if (top == null)
        {
            city.NextStart = NextStart(Schedule(context, city), city.NextStart.AddMinutes(1));
            city.Entries.Clear();
            GuildDirectory.Save();
            return;
        }
        city.Attacker = top.Guild;
        city.AttackerUnits = top.Units;
        city.Phase = GuildWarPhase.Preparing;
        city.Committed.Clear();
        GuildDirectory.Save();
        Bulletin(context, $"The guild {top.Guild} won the right to besiege {city.Name}"
            + (city.Owner.Length > 0 ? $", ruled by {city.Owner}." : ".")
            + $" The battle begins at {city.BattleStart(Schedule(context, city)):HH:mm}.");
    }

    private static void StartBattle(ServerContext context, GuildWarCity city)
    {
        city.Phase = GuildWarPhase.Battle;
        city.Fighters.Clear();
        Bulletin(context, $"The siege of {city.Name} has begun: {city.Attacker} against {(city.Owner.Length > 0 ? city.Owner : "the city garrison")}.");
        foreach (GuildWarEntry entry in city.Entries.Where(entry => entry.State == GuildWarRegistration.Approved
            && city.Committed.Any(guild => GuildDirectory.Same(guild, entry.Guild))).ToList())
        {
            if (Online(context, entry.Name) is { } member) Enter(member, city);
        }
    }

    private static void Enter(ClientSession session, GuildWarCity city)
    {
        GuildWarRules rules = Rules(session.Context);
        if (!session.Context.Data.Scenes.Exists(rules.WarScene))
        {
            session.SystemMessage("The battlefield is not available on this server.");
            return;
        }
        bool attacker = GuildDirectory.Same(session.Guild?.Name ?? "", city.Attacker);
        if (!city.Fighters.TryGetValue(session.Name, out GuildWarFighter? fighter))
        {
            fighter = new GuildWarFighter { Name = session.Name, Guild = session.Guild?.Name ?? "", Attacker = attacker };
            city.Fighters[session.Name] = fighter;
        }
        if (session.Scene != rules.WarScene) fighter.ReturnScene = session.Scene;
        fighter.Present = true;
        session.Send(ScenePackets.LeaveScene(), "(leave scene for the siege)");
        session.EnterScene(rules.WarScene);
        session.Send(PropertyTable.WriteValues(new PacketWriter(ServerMsg.SceneState), new PropertyBag { ["SceneKind"] = rules.SceneKind }).ToArray(), "(siege scene kind)");
        session.SystemMessage(attacker
            ? $"You joined the attack on {city.Name}. Defeat the defenders before {city.BattleEnd(Schedule(session.Context, city)):HH:mm}."
            : $"You joined the defence of {city.Name}. Hold out until {city.BattleEnd(Schedule(session.Context, city)):HH:mm}.");
    }

    private static void Exit(ClientSession session, GuildWarCity city, string note)
    {
        if (!city.Fighters.TryGetValue(session.Name, out GuildWarFighter? fighter)) return;
        fighter.Present = false;
        if (session.Hp <= 0) session.SetHp(session.MaxHp);
        if (!session.InWorld || session.Scene != Rules(session.Context).WarScene) return;
        int scene = fighter.ReturnScene != 0 && session.Context.Data.Scenes.Exists(fighter.ReturnScene)
            ? fighter.ReturnScene
            : GuildWarData.SceneOf(session.Context.Settings, city.Name);
        if (scene == 0 || !session.Context.Data.Scenes.Exists(scene)) scene = session.Context.Settings.World.StartScene;
        session.Send(ScenePackets.LeaveScene(), note);
        session.EnterScene(scene);
        session.Save();
    }

    private static void Finish(ServerContext context, GuildWarCity city, bool? attackerWins)
    {
        GuildWarRules rules = Rules(context);
        List<GuildWarFighter> attackers = city.Fighters.Values.Where(fighter => fighter.Attacker).ToList();
        List<GuildWarFighter> defenders = city.Fighters.Values.Where(fighter => !fighter.Attacker).ToList();
        int attackKills = attackers.Sum(fighter => fighter.Kills);
        int defenceKills = defenders.Sum(fighter => fighter.Kills);
        bool won = attackerWins ?? (city.Owner.Length == 0
            ? attackers.Count > 0
            : attackKills > defenceKills || (attackers.Count > 0 && defenders.Count == 0));
        string defender = city.Owner;
        string winner = won ? city.Attacker : defender;
        string loser = won ? defender : city.Attacker;
        long bid = city.AttackerUnits * rules.BidUnit;
        long reward = bid * rules.WinnerRewardPercent / 100;
        long compensation = bid * rules.LoserCompensationPercent / 100;
        if (winner.Length > 0 && GuildDirectory.Find(context, winner) is { } winning) winning.Fund = (int)Math.Min(int.MaxValue, winning.Fund + reward);
        if (loser.Length > 0 && GuildDirectory.Find(context, loser) is { } losing) losing.Fund = (int)Math.Min(int.MaxValue, losing.Fund + compensation);
        if (won && city.Attacker.Length > 0)
        {
            city.Owner = city.Attacker;
            city.OwnedSince = DateTime.Now;
            city.StatueMessage = "";
        }
        GuildDirectory.Save();

        DateTime started = city.NextStart;
        GuildWarSchedule schedule = Schedule(context, city);
        GuildMember? LeaderOf(string guild) => GuildDirectory.Find(context, guild)?.Members.FirstOrDefault(member => member.Duty == GuildDuty.Leader);
        string[] Side(string guild, List<GuildWarFighter> side)
        {
            GuildWarFighter? best = side.OrderByDescending(fighter => fighter.Merit).ThenByDescending(fighter => fighter.Kills).FirstOrDefault();
            return
            [
                LeaderOf(guild)?.Name ?? "-", side.Count.ToString(), city.Entries.Count(entry => GuildDirectory.Same(entry.Guild, guild)).ToString(),
                side.Sum(fighter => fighter.Kills).ToString(), side.Sum(fighter => fighter.Deaths).ToString(), best?.Name ?? "-", (best?.Merit ?? 0).ToString(),
            ];
        }
        string[] page0 =
        [
            winner.Length > 0 ? winner : "City garrison", reward.ToString(), loser.Length > 0 ? loser : "City garrison", compensation.ToString(),
            $"{started:yyyy-MM-dd HH:mm}-{city.BattleEnd(schedule):HH:mm}", won ? $"{city.Attacker} took {city.Name}" : $"{city.Name} held",
        ];
        string[] page1 = [.. Side(city.Attacker, attackers), .. Side(defender, defenders)];

        foreach (GuildWarFighter fighter in city.Fighters.Values.ToList())
        {
            if (Online(context, fighter.Name) is not { } session) continue;
            Guild? guild = GuildDirectory.Find(context, fighter.Guild);
            string[] page2 =
            [
                fighter.Name, GuildDuty.Name(guild?.Member(fighter.Name)?.Duty ?? GuildDuty.Member), fighter.Kills.ToString(), fighter.Deaths.ToString(),
                "0", "0", fighter.Merit.ToString(),
            ];
            session.Send(GuildWarPackets.WarStat(0, page0), "(siege report)");
            session.Send(GuildWarPackets.WarStat(1, page1), "(siege report)");
            session.Send(GuildWarPackets.WarStat(2, page2), "(siege report)");
            if (fighter.Present) Exit(session, city, "(siege over)");
        }

        Bulletin(context, won
            ? $"The guild {city.Attacker} conquered {city.Name}{(defender.Length > 0 ? $" from {defender}" : "")}."
            : $"{(defender.Length > 0 ? $"The guild {defender}" : "The garrison")} held {city.Name} against {(city.Attacker.Length > 0 ? city.Attacker : "the attackers")}.");
        Log.Info("GuildWar", $"Siege of {city.Name} over: winner {(winner.Length > 0 ? winner : "garrison")}");

        city.Phase = GuildWarPhase.Bidding;
        city.Attacker = "";
        city.AttackerUnits = 0;
        city.Entries.Clear();
        city.Committed.Clear();
        city.Fighters.Clear();
        city.NextStart = NextStart(schedule, DateTime.Now.AddMinutes(1));
    }

    private static DateTime NextStart(GuildWarSchedule schedule, DateTime after)
    {
        DateTime day = after.Date;
        for (int offset = 0; offset <= 7; offset++)
        {
            DateTime candidate = day.AddDays(offset).AddHours(schedule.Hour).AddMinutes(schedule.Minute);
            if ((int)candidate.DayOfWeek == schedule.Week && candidate > after) return candidate;
        }
        return day.AddDays(7).AddHours(schedule.Hour).AddMinutes(schedule.Minute);
    }

    private static GuildWarStanding Standing(ServerContext context, string guild)
    {
        List<GuildWarStanding> standings = Store(context).Guilds;
        GuildWarStanding? standing = standings.FirstOrDefault(entry => GuildDirectory.Same(entry.Guild, guild));
        if (standing == null) standings.Add(standing = new GuildWarStanding { Guild = guild });
        string week = ISOWeek.GetYear(DateTime.Now) + "-" + ISOWeek.GetWeekOfYear(DateTime.Now);
        if (standing.Week != week)
        {
            standing.Week = week;
            standing.EntriesUsed = 0;
        }
        return standing;
    }

    private static bool Leader(ClientSession session, int duty, string denied, out Guild guild)
    {
        Guild? found = session.Guild;
        guild = found!;
        GuildMember? self = found?.Member(session.Name);
        if (found == null || self == null)
        {
            session.SystemMessage("You are not in a guild.");
            return false;
        }
        if (self.Duty >= duty) return true;
        session.SystemMessage(denied);
        return false;
    }

    private static void PublishRegistrations(ServerContext context, Guild guild)
    {
        foreach (ClientSession member in context.World.Sessions.Where(session => session.InWorld && guild.Member(session.Name) != null).ToList())
            ShowRegistrations(member);
    }

    private static ClientSession? Online(ServerContext context, string name)
        => context.World.FindPlayer(name) is { InWorld: true } session ? session : null;

    private static void Tell(ServerContext context, Guild guild, string text)
    {
        foreach (ClientSession member in context.World.Sessions.Where(session => session.InWorld && guild.Member(session.Name) != null).ToList())
            member.SystemMessage(text);
    }

    private static void Bulletin(ServerContext context, string text)
    {
        byte[] bulletin = GuildWarPackets.Bulletin(text, Rules(context).BulletinKind);
        foreach (ClientSession session in context.World.Sessions.Where(session => session.InWorld).ToList())
        {
            session.Send(bulletin, "(siege bulletin)");
            session.SystemMessage(text, SystemMessageKind.System);
        }
    }

    private static GuildWarStore Store(ServerContext context)
    {
        if (_store != null) return _store;
        _path = context.Settings.Resolve("data\\guildwars.json");
        _store = new GuildWarStore();
        try
        {
            if (File.Exists(_path))
                _store = JsonSerializer.Deserialize<GuildWarStore>(File.ReadAllText(_path), EmulatorSettings.Json) ?? new GuildWarStore();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("GuildWar", $"Load failed: {exception.Message}");
        }
        GuildWarRules rules = Rules(context);
        bool changed = false;
        foreach (string name in rules.SiegeCities.Where(name => _store.Cities.All(city => !GuildDirectory.Same(city.Name, name))))
        {
            _store.Cities.Add(new GuildWarCity { Name = name, NextStart = NextStart(rules.Schedule, DateTime.Now) });
            changed = true;
        }
        foreach (GuildWarCity city in _store.Cities.Where(city => city.Phase == GuildWarPhase.Battle))
        {
            city.Phase = GuildWarPhase.Preparing;
            city.NextStart = DateTime.Now.AddMinutes(-(Schedule(context, city).Prepare + Schedule(context, city).Recon));
            changed = true;
        }
        if (changed) Save();
        return _store;
    }

    private static void Save()
    {
        if (_store == null || _path.Length == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_store, EmulatorSettings.Json));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error("GuildWar", $"Save failed: {exception.Message}");
        }
    }
}
