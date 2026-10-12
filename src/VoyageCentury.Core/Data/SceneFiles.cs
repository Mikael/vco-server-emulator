using System.Numerics;
using System.Text;

namespace VoyageCentury.Core.Data;

public sealed class SceneFiles
{
    private const int MaterialSize = 56;
    private const int LightSize = 60;
    private const int LabelSize = 108;
    private const int ObjectSize = 104;

    private readonly string _resourcePath;
    private readonly HashSet<int> _seaScenes = [];
    private readonly Dictionary<int, Vector3?> _spawnPoints = [];

    public SceneFiles(string resourcePath)
    {
        _resourcePath = resourcePath;
        string folder = Path.Combine(resourcePath, "ini", "scenes");
        if (!Directory.Exists(folder)) return;
        foreach (string file in Directory.EnumerateFiles(folder, "*.ini"))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out int scene)) continue;
            string header = File.ReadAllText(file, Encoding.Latin1).ToUpperInvariant().Split('[')[0];
            var words = header.Split('\n').Select(line => line.Trim()).ToHashSet();
            if (words.Contains("SEA") && !words.Contains("CITY")) _seaScenes.Add(scene);
        }
    }

    public int SeaSceneCount => _seaScenes.Count;

    public bool Exists(int scene) => File.Exists(IniPath(scene));

    public bool IsSea(int scene) => _seaScenes.Contains(scene);

    public Vector3 SpawnPoint(int scene)
    {
        lock (_spawnPoints)
        {
            if (!_spawnPoints.TryGetValue(scene, out Vector3? point))
                _spawnPoints[scene] = point = FindSpawnPoint(scene);
            return point ?? Vector3.Zero;
        }
    }

    private string IniPath(int scene) => Path.Combine(_resourcePath, "ini", "scenes", scene + ".ini");

    private Dictionary<string, string> Models(int scene)
    {
        var models = new Dictionary<string, string>();
        string ini = IniPath(scene);
        if (!File.Exists(ini)) return models;
        string modelPath = "";
        foreach (string line in File.ReadLines(ini, Encoding.Latin1))
        {
            int equals = line.IndexOf('=');
            if (equals < 0) continue;
            string key = line[..equals].Trim().ToUpperInvariant();
            string value = line[(equals + 1)..].Split(';')[0].Trim();
            if (key == "MODELPATH") modelPath = value;
            else if (key == "FILENAME" && value.Length > 0)
                models[value.ToLowerInvariant()] = Path.Combine(_resourcePath, "MODELS", modelPath, value + ".GM");
        }
        return models;
    }

    private Vector3? FindSpawnPoint(int scene)
    {
        Dictionary<string, string> models = Models(scene);
        foreach (string name in new[] { "walk_patch", "walkpatch", "walk" })
        {
            if (!models.TryGetValue(name, out string? path)) continue;
            List<Vector3> vertices = ReadVertices(path);
            if (vertices.Count < 3) continue;
            float middleX = vertices.Select(v => v.X).Order().ElementAt(vertices.Count / 2);
            float middleZ = vertices.Select(v => v.Z).Order().ElementAt(vertices.Count / 2);
            return vertices.MinBy(v => (v.X - middleX) * (v.X - middleX) + (v.Z - middleZ) * (v.Z - middleZ));
        }
        return null;
    }

    private static List<Vector3> ReadVertices(string path)
    {
        var vertices = new List<Vector3>();
        try
        {
            byte[] data = File.ReadAllBytes(path);
            int[] header = new int[9];
            for (int i = 0; i < 9; i++) header[i] = BitConverter.ToInt32(data, 8 + 4 * i);
            (int nameSize, int names, int textures, int materials, int lights, int labels, int objects, int triangles, int buffers) =
                (header[0], header[1], header[2], header[3], header[4], header[5], header[6], header[7], header[8]);

            int offset = 0x48 + nameSize + 4 * names + 4 * textures + MaterialSize * materials + LightSize * lights;
            offset += LabelSize * labels;

            var counts = new List<(int Buffer, int Vertices)>();
            for (int i = 0; i < objects; i++)
            {
                counts.Add((BitConverter.ToInt32(data, offset + 28), BitConverter.ToInt32(data, offset + 40)));
                offset += ObjectSize;
            }
            offset += 6 * triangles;

            var sizes = new List<int>();
            for (int i = 0; i < buffers; i++) sizes.Add(BitConverter.ToInt32(data, offset + 8 * i + 4));
            offset += 8 * buffers;

            for (int i = 0; i < buffers; i++)
            {
                int total = counts.Where(count => count.Buffer == i).Sum(count => count.Vertices);
                int stride = total > 0 ? sizes[i] / total : 36;
                if (stride >= 12)
                {
                    for (int k = 0; k < sizes[i] / stride; k++)
                    {
                        int at = offset + k * stride;
                        vertices.Add(new Vector3(BitConverter.ToSingle(data, at), BitConverter.ToSingle(data, at + 4), BitConverter.ToSingle(data, at + 8)));
                    }
                }
                offset += sizes[i];
            }
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            vertices.Clear();
        }
        return vertices;
    }
}
