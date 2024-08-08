using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using ImGuiNET;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Skeletons;
using SharpGLTF.Transforms;
using Attach = Meddle.Plugin.Skeleton.Attach;
using CustomizeData = Meddle.Utils.Export.CustomizeData;

namespace Meddle.Plugin.Utils;

public static class UiUtil
{
    public static void Text(string text, string? copyValue)
    {
        ImGui.Text(text);
        if (ImGui.IsItemHovered() && copyValue != null)
        {
            ImGui.SetTooltip($"Click to copy {copyValue}");
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                ImGui.SetClipboardText(copyValue);
            }
        }
    }
    
    public static void DrawCustomizeParams(ref CustomizeParameter customize)
    {
        ImGui.ColorEdit3("Skin Color", ref customize.SkinColor);
        ImGui.ColorEdit4("Lip Color", ref customize.LipColor);
        ImGui.ColorEdit3("Main Color", ref customize.MainColor);
        ImGui.ColorEdit3("Mesh Color", ref customize.MeshColor);
        ImGui.ColorEdit4("Left Color", ref customize.LeftColor);
        ImGui.BeginDisabled();
        ImGui.ColorEdit4("Right Color", ref customize.RightColor);
        //ImGui.ColorEdit3("Hair Fresnel Value", ref customize.HairFresnelValue0);
        //ImGui.DragFloat("Muscle Tone", ref customize.MuscleTone, 0.01f, 0f, 1f);
        //ImGui.ColorEdit4("Skin Fresnel Value", ref customize.SkinFresnelValue0);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled("?");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(
                "Right Eye Color will not apply to computed textures as it is selected using the vertex shaders");
            ImGui.EndTooltip();
        }

        ImGui.ColorEdit3("Option Color", ref customize.OptionColor);
    }

    public static void DrawCustomizeData(CustomizeData customize)
    {
        ImGui.Checkbox("Lipstick", ref customize.LipStick);
        ImGui.Checkbox("Highlights", ref customize.Highlights);
    }

    public static void DrawColorTable(ColorTable table, ColorDyeTable? dyeTable = null)
    {
        DrawColorTable(table.Rows, dyeTable);
    }

    public static void DrawColorTable(ColorTableRow[] tableRows, ColorDyeTable? dyeTable = null)
    {
        if (ImGui.BeginTable("ColorTable", 9, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Row", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Diffuse", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Specular", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Emissive", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Material Repeat", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Material Skew", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Specular Strength", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Gloss", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Tile Set", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableHeadersRow();

            for (var i = 0; i < tableRows.Length; i++)
            {
                DrawRow(i, ref tableRows[i], dyeTable);
            }

            ImGui.EndTable();
        }
    }

    public static void DrawColorTable(MtrlFile file)
    {
        ImGui.Text($"Color Table: {file.HasTable}");
        ImGui.Text($"Dye Table: {file.HasDyeTable}");
        ImGui.Text($"Extended Color Table: {file.LargeColorTable}");
        if (!file.HasTable)
        {
            return;
        }

        DrawColorTable(file.ColorTable, file.HasDyeTable ? file.ColorDyeTable : null);
    }

    private static void DrawRow(int i, ref ColorTableRow row, ColorDyeTable? dyeTable)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text($"{i}");
        ImGui.TableSetColumnIndex(1);
        ImGui.ColorButton("##rowdiff", new Vector4(row.Diffuse, 1f), ImGuiColorEditFlags.NoAlpha);
        if (dyeTable != null)
        {
            ImGui.SameLine();
            var diff = dyeTable.Value[i].Diffuse;
            ImGui.Checkbox("##rowdiff", ref diff);
        }

        ImGui.TableSetColumnIndex(2);
        ImGui.ColorButton("##rowspec", new Vector4(row.Specular, 1f), ImGuiColorEditFlags.NoAlpha);
        if (dyeTable != null)
        {
            ImGui.SameLine();
            var spec = dyeTable.Value[i].Specular;
            ImGui.Checkbox("##rowspec", ref spec);
        }

        ImGui.TableSetColumnIndex(3);
        ImGui.ColorButton("##rowemm", new Vector4(row.Emissive, 1f), ImGuiColorEditFlags.NoAlpha);
        if (dyeTable != null)
        {
            ImGui.SameLine();
            var emm = dyeTable.Value[i].Emissive;
            ImGui.Checkbox("##rowemm", ref emm);
        }

        ImGui.TableSetColumnIndex(4);
        ImGui.Text($"{row.MaterialRepeat}");
        ImGui.TableSetColumnIndex(5);
        ImGui.Text($"{row.MaterialSkew}");
        ImGui.TableSetColumnIndex(6);
        if (dyeTable != null)
        {
            var specStrength = dyeTable.Value[i].SpecularStrength;
            ImGui.Checkbox("##rowspecstr", ref specStrength);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.SpecularStrength}");
        ImGui.TableSetColumnIndex(7);
        if (dyeTable != null)
        {
            var gloss = dyeTable.Value[i].Gloss;
            ImGui.Checkbox("##rowgloss", ref gloss);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.GlossStrength}");
        ImGui.TableSetColumnIndex(8);
        ImGui.Text($"{row.TileIndex}");
    }
    
    public static unsafe void DrawCharacterAttaches(Character* charPtr)
    {
        var cBase = (CharacterBase*)charPtr->GameObject.DrawObject;
        DrawCharacterBase(cBase, "Main");
        DrawOrnamentContainer(charPtr->OrnamentData);
        DrawCompanionContainer(charPtr->CompanionData);
        DrawMountContainer(charPtr->Mount);
        DrawDrawDataContainer(charPtr->DrawData);
    }
    
    private static unsafe void DrawDrawDataContainer(DrawDataContainer drawDataContainer)
    {
        if (drawDataContainer.OwnerObject == null)
        {
            ImGui.Text($"[DrawDataContainer] Owner is null");
            return;
        }
        
        var ownerObject = drawDataContainer.OwnerObject;
        if (ownerObject == null)
        {
            ImGui.Text($"[DrawDataContainer] Owner is null");
            return;
        }
        
        var weaponData = drawDataContainer.WeaponData;
        foreach (var weapon in weaponData)
        {
            var weaponDrawObject = weapon.DrawObject;
            if (weaponDrawObject == null)
            {
                continue;
            }
            
            var objectType = weaponDrawObject->GetObjectType();
            if (objectType != ObjectType.CharacterBase)
            {
                ImGui.Text($"[Weapon:{weapon.ModelId.Id}] Weapon is not a CharacterBase ({objectType})");
                return;
            }

            DrawCharacterBase((CharacterBase*)weaponDrawObject, "Weapon");
        }
    }
    
    private static unsafe void DrawCompanionContainer(CompanionContainer companionContainer)
    {
        var owner = companionContainer.OwnerObject;
        if (owner == null)
        {
            ImGui.Text($"[Companion:{companionContainer.CompanionId}] Owner is null");
            return;
        }
        var companion = companionContainer.CompanionObject;
        if (companion == null)
        {
            return;
        }

        var objectType = companion->DrawObject->GetObjectType();
        if (objectType != ObjectType.CharacterBase)
        {
            ImGui.Text($"[Companion:{companionContainer.CompanionId}] Companion is not a CharacterBase ({objectType})");
            return;
        }

        DrawCharacterBase((CharacterBase*)companion->DrawObject, "Companion");
    }
    
    private static unsafe void DrawMountContainer(MountContainer mountContainer)
    {
        var owner = mountContainer.OwnerObject;
        if (owner == null)
        {
            ImGui.Text($"[Mount:{mountContainer.MountId}] Owner is null");
            return;
        }
        var mount = mountContainer.MountObject;
        if (mount == null)
        {
            return;
        }
        
        var drawObject = mount->DrawObject;
        if (drawObject == null)
        {
            ImGui.Text($"[Mount:{mountContainer.MountId}] DrawObject is null");
            return;
        }
        
        var objectType = drawObject->GetObjectType();
        if (objectType != ObjectType.CharacterBase)
        {
            ImGui.Text($"[Mount:{mountContainer.MountId}] Mount is not a CharacterBase ({objectType})");
            return;
        }

        DrawCharacterBase((CharacterBase*)drawObject, "Mount");
    }

    private static unsafe void DrawOrnamentContainer(OrnamentContainer ornamentContainer)
    {
        var owner = ornamentContainer.OwnerObject;
        if (owner == null)
        {
            ImGui.Text($"[Ornament:{ornamentContainer.OrnamentId}] Owner is null");
            return;
        }
        var ornament = ornamentContainer.OrnamentObject;
        if (ornament == null)
        {
            return;
        }

        DrawCharacterBase((CharacterBase*)ornament->DrawObject, "Ornament");
    }

    private static unsafe void DrawCharacterBase(CharacterBase* character, string name)
    {
        if (character == null)
            return;
        var skeleton = character->Skeleton;
        if (skeleton == null)
            return;

        Attach attachPoint;
        try
        {
            attachPoint = new Attach(character->Attach);
        }
        catch (Exception e)
        {
            ImGui.Text($"Failed to parse attach: {e}");
            return;
        }

        var modelType = character->GetModelType();
        var attachHeader = $"[{modelType}]{name} Attach Pose ({attachPoint.ExecuteType},{attachPoint.AttachmentCount})";
        if (character->Attach.ExecuteType >= 3)
        {
            var attachedPartialSkeleton = attachPoint.OwnerSkeleton!.PartialSkeletons[attachPoint.PartialSkeletonIdx];
            var boneName = attachedPartialSkeleton.HkSkeleton!.BoneNames[(int)attachPoint.BoneIdx];
            attachHeader += $" at {boneName}";
        }
        else if (character->Attach.ExecuteType == 3)
        {
            var attachedPartialSkeleton = attachPoint.OwnerSkeleton!.PartialSkeletons[attachPoint.PartialSkeletonIdx];
            if (attachedPartialSkeleton.HkSkeleton != null && attachPoint.BoneIdx < attachedPartialSkeleton.HkSkeleton.BoneNames.Count)
            {
                var boneName = attachedPartialSkeleton.HkSkeleton.BoneNames[(int)attachPoint.BoneIdx];
                attachHeader += $" at {boneName}";
            }
            else
            {
                attachHeader += $" at {attachPoint.BoneIdx} > {attachedPartialSkeleton.HandlePath}";
            }
        }

        if (ImGui.CollapsingHeader(attachHeader))
        {
            using var attachId = ImRaii.PushId($"{(nint)character:X8}_Attach");
            DrawAttachInfo(character, attachPoint);
            using var attachIndent = ImRaii.PushIndent();
            if (attachPoint.TargetSkeleton != null && ImGui.CollapsingHeader($"Target Skeleton {(nint)character->Attach.TargetSkeleton:X8}"))
            {
                using var id = ImRaii.PushId($"{(nint)character:X8}_Target");
                DrawSkeleton(attachPoint.TargetSkeleton);
            }

            if (attachPoint.OwnerSkeleton != null && ImGui.CollapsingHeader($"Owner Skeleton {(nint)character->Attach.OwnerSkeleton:X8}"))
            {
                using var id = ImRaii.PushId($"{(nint)character:X8}_Owner");
                DrawSkeleton(attachPoint.OwnerSkeleton);
            }
        }
    }

    private static unsafe void DrawAttachInfo(CharacterBase* character, Attach attachPoint)
    {
        var position = character->Position;
        var rotation = character->Rotation;
        var scale = character->Scale;
        var aTransform = new AffineTransform(scale, rotation, position);
        var transform = new Transform(aTransform);
        ImGui.Text($"Attachment Count: {attachPoint.AttachmentCount}");
        ImGui.Text($"ExecuteType: {attachPoint.ExecuteType}");
        ImGui.Text($"SkeletonIdx: {attachPoint.PartialSkeletonIdx}");
        ImGui.Text($"BoneIdx: {attachPoint.BoneIdx}");
        ImGui.Text($"World Transform: {transform}");
        ImGui.Text($"Root: {attachPoint.OffsetTransform?.ToString() ?? "None"}");
        if (attachPoint.TargetSkeleton != null)
        {
            DrawSkeleton(attachPoint.TargetSkeleton);
        }
        else
        {
            var characterSkeleton = new Skeleton.Skeleton(character->Skeleton);
            DrawSkeleton(characterSkeleton);
        }
        //DrawModels(character);
    }
    
        public static void DrawSkeleton(Skeleton.Skeleton skeleton)
    {
        using var skeletonIndent = ImRaii.PushIndent();
        ImGui.Text($"Partial Skeletons: {skeleton.PartialSkeletons.Count}");
        ImGui.Text($"Transform: {skeleton.Transform}");
        for (int i = 0; i < skeleton.PartialSkeletons.Count; i++)
        {
            var partial = skeleton.PartialSkeletons[i];
            if (partial.HandlePath == null)
            {
                continue;
            }
            using var partialIndent = ImRaii.PushIndent();
            using var partialId = ImRaii.PushId(i);
            if (ImGui.CollapsingHeader($"[{i}]Partial: {partial.HandlePath}"))
            {
                ImGui.Text($"Connected Bone Index: {partial.ConnectedBoneIndex}");
                var poseData = partial.Poses.FirstOrDefault();
                if (poseData == null) continue;
                for (int j = 0; j < poseData.Pose.Count; j++)
                {
                    var transform = poseData.Pose[j];
                    var boneName = partial.HkSkeleton?.BoneNames[j] ?? "Bone";
                    ImGui.Text($"[{j}]{boneName} {transform}");
                }
            }
        }
    }
    
    private static unsafe void DrawSkeleton(FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton* sk, string context)
    {
        using var skeletonIndent = ImRaii.PushIndent();
        using var skeletonId = ImRaii.PushId($"{(nint)sk:X8}");
        ImGui.Text($"Partial Skeletons: {sk->PartialSkeletonCount}");
        ImGui.Text($"Transform: {new Transform(sk->Transform)}");
        for (var i = 0; i < sk->PartialSkeletonCount; ++i)
        {
            using var partialId = ImRaii.PushId($"PartialSkeleton_{i}");
            var handle = sk->PartialSkeletons[i].SkeletonResourceHandle;
            if (handle == null)
            {
                continue;
            }

            if (ImGui.CollapsingHeader($"Partial {i}: {handle->FileName.ToString()}"))
            {
                var p = sk->PartialSkeletons[i].GetHavokPose(0);
                if (p != null && p->Skeleton != null)
                {
                    for (var j = 0; j < p->Skeleton->Bones.Length; ++j)
                    {
                        var boneName = p->Skeleton->Bones[j].Name.String ?? $"Bone {j}";
                        ImGui.TextUnformatted($"[{i}, {j}] => {boneName}");
                    }
                }
            }
        }
    }
}
