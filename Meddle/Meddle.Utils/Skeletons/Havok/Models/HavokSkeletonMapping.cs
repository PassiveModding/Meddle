namespace Meddle.Utils.Skeletons.Havok.Models;

public class HavokSkeletonMapping
{
    public int           Id { get; set; }
    public int           SkeletonA { get; set; }
    public int           SkeletonB { get; set; }
    public BoneMapping[] BoneMappings { get; set; }
    
    public class BoneMapping {
        public readonly int     BoneA;
        public readonly int     BoneB;
        public readonly float[] Transform;

        public BoneMapping( int boneA, int boneB, float[] transform ) {
            BoneA     = boneA;
            BoneB     = boneB;
            Transform = transform;
        }
    }
}
