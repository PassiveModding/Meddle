using Lumina.Data.Files;
using Lumina.Models.Models;
using Penumbra.Api;
using SharpGLTF.Scenes;
using SharpGLTF.Transforms;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Xande;
using Xande.Havok;

namespace Meddle.Xande.Utility;

public static class ModelUtility
{
    /// <summary>Builds a skeleton tree from a list of .sklb paths.</summary>
    /// <param name="skeletons">A list of HavokXml instances.</param>
    /// <param name="root">The root bone node.</param>
    /// <returns>A mapping of bone name to node in the scene.</returns>
    public static Dictionary<string, NodeBuilder> GetBoneMap(IEnumerable<HavokXml> skeletons, Dictionary<string, AffineTransform> boneLookup, out NodeBuilder? root)
    {
        Dictionary<string, NodeBuilder> boneMap = new();
        root = null;

        foreach (var xml in skeletons)
        {
            var skeleton = xml.GetMainSkeleton();
            var boneNames = skeleton.BoneNames;
            var refPose = skeleton.ReferencePose;
            var parentIndices = skeleton.ParentIndices;

            for (var j = 0; j < boneNames.Length; j++)
            {
                var name = boneNames[j];
                if (boneMap.ContainsKey(name)) continue;

                var bone = new NodeBuilder(name);
                bone.UseScale();
                bone.UseRotation();
                bone.UseTranslation();
                bone.SetLocalTransform(XmlUtils.CreateAffineTransform(refPose[j]).GetDecomposed(), false);

                if (boneLookup.TryGetValue(boneNames[j], out var affine))
                {
                    if (!affine.TryDecompose(out var scale, out var rotation, out var translation))
                        throw new InvalidOperationException("Failed to decompose transform.");

                    bone.Scale.UseTrackBuilder("pose").WithPoint(0, scale);
                    bone.Rotation.UseTrackBuilder("pose").WithPoint(0, rotation);
                    bone.Translation.UseTrackBuilder("pose").WithPoint(0, translation);
                }

                var boneRootId = parentIndices[j];
                if (boneRootId != -1)
                {
                    var parent = boneMap[boneNames[boneRootId]];
                    parent.AddNode(bone);
                }
                else
                {
                    root = bone;
                }

                boneMap[name] = bone;
            }
        }

        return boneMap;
    }

    /// <summary>Builds a skeleton tree from a list of .sklb paths.</summary>
    /// <param name="skeletons">A list of HavokXml instances.</param>
    /// <param name="root">The root bone node.</param>
    /// <returns>A mapping of bone name to node in the scene.</returns>
    public static Dictionary<string, NodeBuilder> GetReferenceBoneMap(IEnumerable<HavokXml> skeletons, out NodeBuilder? root)
    {
        Dictionary<string, NodeBuilder> boneMap = new();
        root = null;

        foreach (var xml in skeletons)
        {
            var skeleton = xml.GetMainSkeleton();
            var boneNames = skeleton.BoneNames;
            var refPose = skeleton.ReferencePose;
            var parentIndices = skeleton.ParentIndices;

            for (var j = 0; j < boneNames.Length; j++)
            {
                var name = boneNames[j];
                if (boneMap.ContainsKey(name)) continue;

                var bone = new NodeBuilder(name);
                bone.SetLocalTransform(XmlUtils.CreateAffineTransform(refPose[j]), false);

                var boneRootId = parentIndices[j];
                if (boneRootId != -1)
                {
                    var parent = boneMap[boneNames[boneRootId]];
                    parent.AddNode(bone);
                }
                else
                {
                    root = bone;
                }

                boneMap[name] = bone;
            }
        }

        return boneMap;
    }

    public static Dictionary<string, NodeBuilder> GetWeaponBoneMap(HkSkeleton hkSkeleton, out NodeBuilder? root)
    {
        var prefix = $"weapon_{unchecked((uint)hkSkeleton.WeaponInfo!.GetHashCode()):X8}_";

        Dictionary<string, NodeBuilder> boneMap = new();
        NodeBuilder? skRoot = null;

        var skeleton = hkSkeleton.Xml.GetMainSkeleton();
        var boneNames = skeleton.BoneNames;
        var refPose = skeleton.ReferencePose;
        var parentIndices = skeleton.ParentIndices;

        for (var j = 0; j < boneNames.Length; j++)
        {
            var name = boneNames[j];
            if (boneMap.ContainsKey(name)) continue;

            var bone = new NodeBuilder($"{prefix}{name}");
            bone.UseScale();
            bone.UseRotation();
            bone.UseTranslation();
            bone.SetLocalTransform(XmlUtils.CreateAffineTransform(refPose[j]).GetDecomposed(), false);

            if (!hkSkeleton.WeaponInfo.BoneLookup[boneNames[j]].TryDecompose(out var scale, out var rotation, out var translation))
                throw new InvalidOperationException("Failed to decompose transform.");

            bone.Scale.UseTrackBuilder("pose").WithPoint(0, scale);
            bone.Rotation.UseTrackBuilder("pose").WithPoint(0, rotation);
            bone.Translation.UseTrackBuilder("pose").WithPoint(0, translation);

            var boneRootId = parentIndices[j];
            if (boneRootId != -1)
            {
                var parent = boneMap[boneNames[boneRootId]];
                parent.AddNode(bone);
            }
            else
            {
                skRoot = bone;
            }

            boneMap[name] = bone;
        }


        root = new NodeBuilder($"{prefix}root");
        root.SetLocalTransform(hkSkeleton.WeaponInfo.AttachOffset, false);
        root.AddNode(skRoot);

        return boneMap;
    }

    private static readonly object ModelLoadLock = new();
    public static Model GetModel(LuminaManager manager, string path)
    {
        lock (ModelLoadLock)
        {
            var mdlFile = manager.GetFile<MdlFile>(path);
            return mdlFile != null
                ? new Model(mdlFile)
                : throw new FileNotFoundException();
        }
    }

    public static bool TryGetModel(this LuminaManager manager, Ipc.ResourceNode node, ushort? deform, out string path, [MaybeNullWhen(false)] out Model model)
    {
        path = node.FullPath();
        if (TryLoadModel(node.FullPath(), out model)) { return true; }

        if (TryLoadModel(node.GamePath ?? string.Empty, out model)) { return true; }

        if (TryLoadRacialModel(node.GamePath ?? string.Empty, deform, out _, out model)) { return true; }

        return false;

        bool TryLoadRacialModel(string path, ushort? cDeform, out string nPath, [MaybeNullWhen(false)] out Model model)
        {
            nPath = path;
            model = null;
            if (cDeform == null) { return false; }

            nPath = Regex.Replace(path, @"c\d+", $"c{cDeform}");
            try
            {
                model = GetModel(manager, nPath);
                return true;
            }
            catch { return false; }
        }

        bool TryLoadModel(string path, [MaybeNullWhen(false)] out Model model)
        {
            model = null;
            try
            {
                model = GetModel(manager, path);
                return true;
            }
            catch
            {
                return false;
            }
        }

    }
}
