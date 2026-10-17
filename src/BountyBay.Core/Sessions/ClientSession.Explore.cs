using BountyBay.Core.Data;
using BountyBay.Core.Logging;
using BountyBay.Core.Protocol;
using BountyBay.Core.Protocol.Packets;

namespace BountyBay.Core.Sessions;

public sealed partial class ClientSession
{
    private const int ExploreCulture = 0;
    private const int ExploreIndustry = 1;
    private const int ExploreAnimal = 2;
    private const int ExploreBonus = 3;
    private const int ExploreAreaCount = 12;

    private static readonly Dictionary<int, Func<int, bool>> ExploreAreaScenes = new()
    {
        [4] = scene => scene / 1000 == 1601,
        [7] = scene => scene == 1001057,
        [10] = scene => scene == 1001071 || scene / 1000 == 1045,
    };

    public void SelectExploreArea(int area)
    {
        if (area < 1 || area > ExploreAreaCount)
        {
            SystemMessage("That exploration area does not exist.", SystemMessageKind.Prompt);
            return;
        }
        int[] points = ExploreFor(area);
        Send(ChatPackets.Script(0, $"scExploreMark {points[ExploreCulture]} {points[ExploreIndustry]} {points[ExploreAnimal]} {points[ExploreBonus]}"),
            $"(exploration area {area})");
    }

    public void ClaimExploreReward(string[] words)
    {
        SystemMessage("You have not gathered enough exploration points in this area for that reward yet.", SystemMessageKind.Prompt);
        Log.Info(Tag, $"{Name} asks for exploration reward: {string.Join(' ', words.Skip(1))}");
    }

    private void GainExploreKill(MonsterDefinition monster)
    {
        foreach ((int area, Func<int, bool> inArea) in ExploreAreaScenes)
        {
            if (!inArea(monster.Scene)) continue;
            ExploreFor(area)[ExploreAnimal] += Math.Max(1, monster.Level);
            return;
        }
    }

    private int[] ExploreFor(int area)
    {
        if (!Character.ExplorePoints.TryGetValue(area, out int[]? points) || points.Length < 4)
            Character.ExplorePoints[area] = points = new int[4];
        return points;
    }
}
