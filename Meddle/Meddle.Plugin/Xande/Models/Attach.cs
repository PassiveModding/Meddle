using Dalamud.Logging;

namespace Meddle.Plugin.Xande.Models;

public unsafe class Attach
{
    public int ExecuteType { get; set; }

    public Transform Transform { get; set; }
    public byte PartialSkeletonIdx { get; set; }
    public ushort BoneIdx { get; set; }

    public Attach()
    {

    }

    public Attach(FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Attach* attach)
    {
        ExecuteType = attach->ExecuteType;
        if (ExecuteType == 0)
            return;

        if (attach->ExecuteType != 4)
        {
            PluginLog.Error($"Unsupported ExecuteType {attach->ExecuteType}");
            return;
        }

        var att = attach->SkeletonBoneAttachments[0];

        Transform = new(att.ChildTransform);

        PartialSkeletonIdx = att.SkeletonIdx;
        BoneIdx = att.BoneIdx;
    }
}
