using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok;
using ImGuiNET;
using SharpGLTF.Transforms;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dalamud.Interface.Utility.Raii;
using Meddle.Plugin.Models;
using Meddle.Plugin.Utility;
using Meddle.Plugin.Xande;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using CSTransform = FFXIVClientStructs.FFXIV.Client.Graphics.Transform;
using Skeleton = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton;

namespace Meddle.Plugin.UI;

public unsafe partial class CharacterTab
{
    private static void DrawPoseInfo(Character character)
    {
        var charPtr = (CSCharacter*)character.Address;
        var human = (Human*)charPtr->GameObject.DrawObject;
        var mainPose = GetPose(human->CharacterBase.Skeleton)!;
        var weaponData = charPtr->DrawData.WeaponDataSpan;

        var t = human->CharacterBase.Skeleton->Transform;
        ImGui.TextUnformatted($"Position: {t.Position:0.00}");
        ImGui.TextUnformatted($"Rotation: {t.Rotation.EulerAngles:0.00}");
        ImGui.TextUnformatted($"Scale: {t.Scale:0.00}");

        if (ImGui.CollapsingHeader("Main Pose"))
        {
            DrawSkeletonAsTree(human->CharacterBase.Skeleton);
        }

        if (ImGui.CollapsingHeader("Main Pose Names"))
        {
            var sk = human->CharacterBase.Skeleton;
            ImGui.Text($"{(nint)sk:X8}");
            for (var i = 0; i < sk->PartialSkeletonCount; ++i)
            {
                var p = sk->PartialSkeletons[i].GetHavokPose(0);
                if (p != null && p->Skeleton != null)
                {
                    for (var j = 0; j < p->Skeleton->Bones.Length; ++j)
                        ImGui.TextUnformatted($"[{i:X}, {j:X}] => {p->Skeleton->Bones[j].Name.String ?? $"Bone {j}"}");
                }

                ImGui.Separator();
            }
        }

        if (ImGui.CollapsingHeader("Main Hand"))
            DrawWeaponData(weaponData[0]);

        if (ImGui.CollapsingHeader("Off Hand"))
            DrawWeaponData(weaponData[1]);

        if (ImGui.CollapsingHeader("Prop"))
            DrawWeaponData(weaponData[2]);

        if (ImGui.Button("Copy"))
            ImGui.SetClipboardText(JsonSerializer.Serialize(new CharacterTree(charPtr),
                                                            new JsonSerializerOptions()
                                                                {WriteIndented = true, IncludeFields = true}));
    }
    
    
    public static bool IsHuman(Character obj)
    {
        var drawObject = ((CSCharacter*)obj.Address)->GameObject.DrawObject;
        if (drawObject == null)
            return false;
        if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase)
            return false;
        if (((CharacterBase*)drawObject)->GetModelType() != CharacterBase.ModelType.Human)
            return false;
        return true;
    }

    private static void DrawWeaponData(DrawObjectData data)
    {
        var skeleton = GetWeaponSkeleton(data);
        var pose = GetPose(skeleton);

        if (pose != null)
        {
            ImGui.TextUnformatted(
                $"Draw Object Transform: {new Transform(data.Model->CharacterBase.DrawObject.Object.Transform)}");
            ImGui.TextUnformatted($"{(nint)data.Model:X8}");
            ImGui.TextUnformatted(
                $"Skeleton Transform: {new Transform(data.Model->CharacterBase.Skeleton->Transform)}");
            ImGui.Separator();
            var attach = &data.Model->CharacterBase.Attach;
            var skele = attach->OwnerSkeleton;
            var attachments = attach->SkeletonBoneAttachments;
            for (var i = 0; i < attach->AttachmentCount; ++i)
            {
                var attachment = &attachments[i];
                var havokPose = skele->PartialSkeletonsSpan[attachment->SkeletonIdx].GetHavokPose(0);
                // This is the bone the attachment is connected to
                ImGui.Text($"Skeleton Index: {attachment->SkeletonIdx} Bone Index: {attachment->BoneIdx}");
                ImGui.Text(
                    $"Attached bone: {havokPose->Skeleton->Bones[attachment->BoneIdx].Name.String ?? "Unknown"}");
                ImGui.Text($"Child Transform: {new Transform(attachment->ChildTransform)}");
                var poseMatrix = GetMatrix(*havokPose->CalculateBoneModelSpace(attachment->BoneIdx));
                var offsetMatrix = GetMatrix(attachment->ChildTransform);
                var skeleTransform = GetMatrix(skele->Transform);
                ImGui.Text($"Pose Transform: {new Transform(poseMatrix)}");
                ImGui.Text($"Offset Transform: {new Transform(offsetMatrix)}");
                ImGui.Text($"Skeleton Transform: {new Transform(skeleTransform)}");
                ImGui.Text($"{new Transform(offsetMatrix * poseMatrix * GetMatrix(skele->Transform))}");
                ImGui.Separator();
            }

            ImGui.Text("Child Skeleton");
            /*foreach (var bone in pose)
            {
                var v = new Transform(bone.Value);
                ImGui.TextUnformatted($"{bone.Key} => {v.Translation:0.00} {new EulerAngles(v.Rotation).Angles:0.00} {v.Scale:0.00}");
            }*/
            DrawSkeletonAsTree(skeleton, $"child_{data.Model->GetHashCode()}");
        }
        else
            ImGui.TextUnformatted("No pose");
    }

    private static void DrawSkeletonAsTree(Skeleton* skeleton, string ctx = "Main")
    {
        var mSkele = new Meddle.Plugin.Models.Skeleton(skeleton);
        var map = ModelUtility.GetBoneMap(mSkele, out var root);

        if (root == null)
            return;

        ImGui.TextUnformatted("Root Bone");

        DrawBoneNode(root, ctx);
    }

    private static void DrawBoneNode(BoneNodeBuilder node, string ctx)
    {
        var pose = new Transform(node.GetWorldMatrix("pose", 0));
        var refPose = node.LocalTransform;
        var name = $"{node.BoneName}##{ctx}";

        if (node.VisualChildren.Count == 0)
        {
            using var tree = ImRaii.TreeNode(name);
            if (tree)
            {
                using (ImRaii.TreeNode(
                           $"Pose: " +
                           $"{pose.Translation:0.00} " +
                           $"{new EulerAngles(pose.Rotation).Angles:0.00} " +
                           $"{pose.Scale:0.00}",
                           ImGuiTreeNodeFlags.Leaf)) { }

                using (ImRaii.TreeNode(
                           $"Ref: {refPose.Translation:0.00} " +
                           $"{new EulerAngles(refPose.Rotation).Angles:0.00} " +
                           $"{refPose.Scale:0.00}",
                           ImGuiTreeNodeFlags.Leaf)) { }
            }
        }
        else
        {
            using var tree = ImRaii.TreeNode(name);
            if (tree)
            {
                using (ImRaii.TreeNode($"Pose: " +
                                       $"{pose.Translation:0.00} " +
                                       $"{new EulerAngles(pose.Rotation).Angles:0.00} " +
                                       $"{pose.Scale:0.00}",
                                       ImGuiTreeNodeFlags.Leaf)) { }

                using (ImRaii.TreeNode($"Ref: " +
                                       $"{refPose.Translation:0.00} " +
                                       $"{new EulerAngles(refPose.Rotation).Angles:0.00} " +
                                       $"{refPose.Scale:0.00}",
                                       ImGuiTreeNodeFlags.Leaf)) { }

                foreach (var child in node.VisualChildren)
                {
                    var cb = (BoneNodeBuilder)child;
                    DrawBoneNode(cb, ctx);
                }
            }
        }
    }

    private static Skeleton* GetWeaponSkeleton(DrawObjectData data)
    {
        return data.Model == null ? null : data.Model->CharacterBase.Skeleton;
    }

    private static Vector3 AsVector(hkVector4f hkVector) =>
        new(hkVector.X, hkVector.Y, hkVector.Z);

    private static Vector4 AsVector(hkQuaternionf hkVector) =>
        new(hkVector.X, hkVector.Y, hkVector.Z, hkVector.W);

    private static Quaternion AsQuaternion(hkQuaternionf hkQuaternion) =>
        new(hkQuaternion.X, hkQuaternion.Y, hkQuaternion.Z, hkQuaternion.W);

    private static Matrix4x4 GetMatrix(CSTransform transform) =>
        new Transform(transform).AffineTransform.Matrix;

    private static unsafe Matrix4x4 GetMatrix(hkQsTransformf transform)
    {
        var transformPtr = (hkQsTransformf*)NativeMemory.AlignedAlloc((nuint)sizeof(hkQsTransformf), 32);
        var matrixPtr = (Matrix4x4*)NativeMemory.AlignedAlloc((nuint)sizeof(Matrix4x4), 32);
        *transformPtr = transform;
        *matrixPtr = new();
        transformPtr->get4x4ColumnMajor((float*)matrixPtr);
        var ret = *matrixPtr;
        NativeMemory.AlignedFree(transformPtr);
        NativeMemory.AlignedFree(matrixPtr);
        return ret;
    }

    private static AffineTransform AsAffineTransform(hkQsTransformf hkTransform)
    {
        return new(GetMatrix(hkTransform));
    }

    private static Dictionary<string, hkQsTransformf>? GetPose(Skeleton* skeleton)
    {
        if (skeleton == null)
            return null;

        var ret = new Dictionary<string, hkQsTransformf>();

        for (var i = 0; i < skeleton->PartialSkeletonCount; ++i)
        {
            var partial = skeleton->PartialSkeletons[i];
            var pose = partial.GetHavokPose(0);
            if (pose == null)
                continue;

            var partialSkele = pose->Skeleton;

            for (var j = 0; j < partialSkele->Bones.Length; ++j)
            {
                if (j == partial.ConnectedBoneIndex)
                    continue;

                var boneName = pose->Skeleton->Bones[j].Name.String;
                if (string.IsNullOrEmpty(boneName))
                    continue;

                var model = pose->AccessBoneLocalSpace(j);
                ret[boneName] = *model;
            }
        }

        return ret;
    }
}
