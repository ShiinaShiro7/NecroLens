using System.Diagnostics.CodeAnalysis;

namespace NecroLensNew.Model;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class MobInfo
{
    public uint Id { get; set; }
    public ESPObject.ESPAggroType? AggroType { get; set; }
    public ESPObject.ESPDangerLevel? DangerLevel { get; set; }
    public bool? Patrol { get; set; }
    public bool? BossOrAdd { get; set; }
    public bool? Special { get; set; }
}
