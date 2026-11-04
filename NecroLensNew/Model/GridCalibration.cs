using System;
using System.Collections.Generic;
using System.Linq;

namespace NecroLensNew.Model;

/**
 * Learns the world-space transform of the 5x5 room grid for one
 * (territory, activeLayoutIndex) pair from player position + RoomIndex
 * samples: cellCenter(i) = Origin + (col(i), row(i)) * Pitch.
 * Both layout copies sit at fixed world coordinates, so the transform
 * is constant per (territory, layout) and persists across floors.
 */
[Serializable]
public class GridCalibration
{
    public const int CellCount = 25;
    private const int MaxPerCell = 60;
    private const int RecomputeEvery = 50;

    /** room index -> sampled world positions [x, z] */
    public Dictionary<int, List<float[]>> Samples = new();

    public bool Valid;
    public float PitchX;
    public float PitchZ;
    public float OriginX;
    public float OriginZ;

    [Newtonsoft.Json.JsonIgnore]
    private int sinceCompute;

    public void AddSample(int room, float x, float z)
    {
        if (room < 0 || room >= CellCount) return;
        if (!Samples.TryGetValue(room, out var list))
            Samples[room] = list = new List<float[]>();

        if (list.Count > 0)
        {
            var last = list[^1];
            if (Math.Abs(last[0] - x) < 0.4f && Math.Abs(last[1] - z) < 0.4f)
                return;
        }

        list.Add(new[] { x, z });
        if (list.Count > MaxPerCell) list.RemoveAt(0);

        if (++sinceCompute >= RecomputeEvery)
            Recompute();
    }

    public void Recompute()
    {
        sinceCompute = 0;
        var cells = Samples.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToList();
        if (cells.Count < 2) return;

        var cx = cells.ToDictionary(i => i, i => Samples[i].Average(s => s[0]));
        var cz = cells.ToDictionary(i => i, i => Samples[i].Average(s => s[1]));

        var pitchXs = new List<float>();
        var pitchZs = new List<float>();
        foreach (var i in cells)
        foreach (var j in cells)
        {
            if (i == j) continue;
            var dc = j % 5 - i % 5;
            var dr = j / 5 - i / 5;
            if (dc != 0) pitchXs.Add((cx[j] - cx[i]) / dc);
            if (dr != 0) pitchZs.Add((cz[j] - cz[i]) / dr);
        }

        if (pitchXs.Count == 0 && pitchZs.Count == 0) return;

        // Rooms are square; if only one axis is constrained mirror it.
        PitchX = pitchXs.Count > 0 ? pitchXs.Average() : pitchZs.Average();
        PitchZ = pitchZs.Count > 0 ? pitchZs.Average() : PitchX;

        var ox = 0f;
        var oz = 0f;
        foreach (var i in cells)
        {
            ox += cx[i] - i % 5 * PitchX;
            oz += cz[i] - i / 5 * PitchZ;
        }
        OriginX = ox / cells.Count;
        OriginZ = oz / cells.Count;
        Valid = true;
    }

    /**
     * Sampled cells use their sample centroid (best local estimate);
     * unsampled cells extrapolate from the fitted grid.
     */
    public (float X, float Z) Center(int i)
    {
        if (Samples.TryGetValue(i, out var list) && list.Count >= 3)
            return (list.Average(s => s[0]), list.Average(s => s[1]));
        return (OriginX + i % 5 * PitchX, OriginZ + i / 5 * PitchZ);
    }

    /**
     * Nearest grid cell for a world position, or -1 when uncalibrated
     * or outside a generous cell tolerance.
     */
    public int TryAssignRoom(float x, float z, float toleranceFactor = 0.85f)
    {
        if (!Valid) return -1;
        var best = -1;
        var bestDist = float.MaxValue;
        for (var i = 0; i < CellCount; i++)
        {
            if (i is 0 or 4 or 20 or 24) continue;
            var (cx, cz) = Center(i);
            var dx = x - cx;
            var dz = z - cz;
            var dist = dx * dx + dz * dz;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        var tol = MathF.Max(MathF.Abs(PitchX), MathF.Abs(PitchZ)) * toleranceFactor;
        return bestDist < tol * tol ? best : -1;
    }
}
