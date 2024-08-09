using Dalamud.Game;
using FFXIVClientStructs.Havok.Animation.Rig;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;

namespace Meddle.Plugin.Utils;

public static class PoseUtil
{
    public static ISigScanner? SigScanner { get; set; } = null!;

    public static unsafe hkQsTransformf* AccessBoneLocalSpace(hkaPose* pose, int boneIdx)
    {
        if (SigScanner == null)
            throw new Exception("SigScanner not set");

        if (SigScanner.TryScanText("4C 8B DC 53 55 56 57 41 54 41 56 48 81 EC", out var accessBoneLocalSpacePtr))
        {
            var accessBoneLocalSpace = (delegate* unmanaged<hkaPose*, int, hkQsTransformf*>)accessBoneLocalSpacePtr;
            return accessBoneLocalSpace(pose, boneIdx);
        }

        throw new Exception("Failed to find hkaPose::AccessBoneLocalSpace");
    }
}
