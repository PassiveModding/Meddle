using SharpGLTF.Transforms;
using Xande.Havok;

namespace Meddle.Xande;

public class HkSkeleton
{
    public record WeaponData
    {
        public required string SklbPath { get; set; }
        public required string ModelPath { get; set; }
        public required string BoneName { get; set; }
        public required AffineTransform AttachOffset { get; set; }
        public required AffineTransform PoseOffset { get; set; }
        public required AffineTransform OwnerOffset { get; set; }
        public required Dictionary<string, AffineTransform> BoneLookup { get; set; }
    }

    public HavokXml Xml { get; set; }
    public WeaponData? WeaponInfo { get; set; }

    public HkSkeleton(HavokXml xml, WeaponData? weaponInfo = null)
    {
        Xml = xml;
        WeaponInfo = weaponInfo;
    }
}
