using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;
using SharpGLTF.Transforms;
using CustomizeData = Meddle.Utils.Export.CustomizeData;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;

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

    public static unsafe void DrawCharacterAttaches(Pointer<Character> characterPointer)
    {
        if (characterPointer == null || characterPointer.Value == null)
        {
            ImGui.Text("Character is null");
            return;
        }

        var drawObject = characterPointer.Value->GameObject.GetDrawObject();
        if (drawObject == null)
        {
            ImGui.Text("DrawObject is null");
            return;
        }

        var objectType = drawObject->GetObjectType();
        if (objectType != ObjectType.CharacterBase)
        {
            ImGui.Text($"Character is not a CharacterBase ({objectType})");
            return;
        }

        using var table = ImRaii.Table("AttachTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("Model Type", ImGuiTableColumnFlags.WidthFixed, 100);
        ImGui.TableSetupColumn("Skeleton");
        ImGui.TableHeadersRow();
        
        var cBase = (CharacterBase*)characterPointer.Value->GameObject.DrawObject;
        DrawCharacterBase(cBase, "Main");
        DrawOrnamentContainer(characterPointer.Value->OrnamentData);
        //DrawCompanionContainer(characterPointer.Value->CompanionData);
        DrawMountContainer(characterPointer.Value->Mount);
        DrawDrawDataContainer(characterPointer.Value->DrawData);
    }

    private static unsafe void DrawDrawDataContainer(DrawDataContainer drawDataContainer)
    {
        if (drawDataContainer.OwnerObject == null)
        {
            ImGui.Text("[DrawDataContainer] Owner is null");
            return;
        }

        var ownerObject = drawDataContainer.OwnerObject;
        if (ownerObject == null)
        {
            ImGui.Text("[DrawDataContainer] Owner is null");
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

    private static unsafe void DrawCharacterBase(Pointer<CharacterBase> characterPointer, string name)
    {
        if (characterPointer == null || characterPointer.Value == null)
            return;
        var character = characterPointer.Value;
        var skeleton = character->Skeleton;
        if (skeleton == null)
            return;

        var attach = characterPointer.GetAttach();
        ParsedAttach attachPoint;
        try
        {
            attachPoint = new ParsedAttach(attach);
        }
        catch (Exception e)
        {
            ImGui.Text($"Failed to parse attach: {e}");
            return;
        }

        var modelType = character->GetModelType();
        var attachType = attachPoint.ExecuteType switch
        {
            0 => "Root",
            3 => "Owner Attach",
            4 => "Skeleton Attach",
            _ => "Unknown"
        };
        

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.Text(name);
        ImGui.TableSetColumnIndex(1);
        ImGui.Text(modelType.ToString());
        ImGui.TableSetColumnIndex(2);
        
        string attachHeader;
        if (attachPoint.ExecuteType != 0)
        {
            var attachedPartialSkeleton = attachPoint.OwnerSkeleton!.PartialSkeletons[attachPoint.PartialSkeletonIdx];
            var boneName = attachedPartialSkeleton.HkSkeleton!.BoneNames[(int)attachPoint.BoneIdx];
            attachHeader = $"[{attachPoint.ExecuteType}]{attachType} at {boneName}";
        }
        else
        {
            attachHeader = $"[{attachPoint.ExecuteType}]{attachType}";
        }
        if (ImGui.CollapsingHeader(attachHeader))
        {
            using var attachId = ImRaii.PushId($"{(nint)character:X8}_Attach");
            DrawAttachInfo(character, attachPoint);
            if (attachPoint.TargetSkeleton != null &&
                ImGui.CollapsingHeader($"Target Skeleton {(nint)attach.TargetSkeleton:X8}"))
            {
                using var id = ImRaii.PushId($"{(nint)character:X8}_Target");
                DrawSkeleton(attachPoint.TargetSkeleton);
            }

            if (attachPoint.OwnerSkeleton != null &&
                ImGui.CollapsingHeader($"Owner Skeleton {(nint)attach.OwnerSkeleton:X8}"))
            {
                using var id = ImRaii.PushId($"{(nint)character:X8}_Owner");
                DrawSkeleton(attachPoint.OwnerSkeleton);
            }
        }
    }

    private static unsafe void DrawAttachInfo(Pointer<CharacterBase> characterPointer, ParsedAttach attachPoint)
    {
        if (characterPointer == null || characterPointer.Value == null)
            return;
        var character = characterPointer.Value;
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
            ImGui.Text($"Skeleton Transform: {attachPoint.TargetSkeleton.Transform}");
            ImGui.Text($"Partial Skeletons: {attachPoint.TargetSkeleton.PartialSkeletons.Count}");
            DrawSkeleton(attachPoint.TargetSkeleton);
        }
        else
        {
            var characterSkeleton = characterPointer.GetParsedSkeleton();
            ImGui.Text($"Skeleton Transform: {characterSkeleton.Transform}");
            ImGui.Text($"Partial Skeletons: {characterSkeleton.PartialSkeletons.Count}");
            DrawSkeleton(characterSkeleton);
        }
    }

    public static void DrawSkeleton(ParsedSkeleton skeleton)
    {
        for (var i = 0; i < skeleton.PartialSkeletons.Count; i++)
        {
            var partial = skeleton.PartialSkeletons[i];
            if (partial.HandlePath == null)
            {
                continue;
            }

            using var partialId = ImRaii.PushId(i);
            if (ImGui.CollapsingHeader($"[{i}]Partial: {partial.HandlePath}"))
            {
                ImGui.Text($"Connected Bone Index: {partial.ConnectedBoneIndex}");
                var poseData = partial.Poses.FirstOrDefault();
                if (poseData == null) continue;
                for (var j = 0; j < poseData.Pose.Count; j++)
                {
                    var transform = poseData.Pose[j];
                    var boneName = partial.HkSkeleton?.BoneNames[j] ?? "Bone";
                    ImGui.Text($"[{j}]{boneName} {transform}");
                    if (ImGui.IsItemHovered())
                    {
                        var parent = partial.HkSkeleton?.BoneParents[j] ?? -1;
                        if (parent != -1)
                        {
                            var parentName = partial.HkSkeleton?.BoneNames[parent] ?? "Bone";
                            ImGui.SetTooltip($"Parent: {parentName} ({parent})");
                        }
                        else
                        {
                            ImGui.SetTooltip("No Parent");
                        }
                    }
                }
            }

            if (ImGui.CollapsingHeader($"[{i}]Partial tree: {partial.HandlePath}"))
            {
                ImGui.Text($"Connected Bone Index: {partial.ConnectedBoneIndex}");
                for (var poseIdx = 0; poseIdx < partial.Poses.Count; poseIdx++)
                {
                    using var poseId = ImRaii.PushId(poseIdx);
                    var pose = partial.Poses[poseIdx];
                    if (ImGui.CollapsingHeader($"Pose: {poseIdx}"))
                    {
                        var roots = new List<int>();
                        for (var j = 0; j < pose.Pose.Count; j++)
                        {
                            if (partial.HkSkeleton?.BoneParents[j] == -1)
                            {
                                roots.Add(j);
                            }
                        }
                        
                        foreach (var root in roots)
                        {
                            using var rootId = ImRaii.PushId(root);
                            DrawBoneTree(partial, pose, root);
                        }
                    }
                }
            }
        }
    }

    private static void DrawBoneTree(ParsedPartialSkeleton partial, ParsedHkaPose pose, int root)
    {
        var boneName = partial.HkSkeleton?.BoneNames[root] ?? "Bone";
        var transform = pose.Pose[root];
                
        var children = new List<int>();
        for (var i = 0; i < partial.HkSkeleton!.BoneParents.Count; i++)
        {
            if (partial.HkSkeleton.BoneParents[i] == root)
            {
                children.Add(i);
            }
        }
        
        var flags = ImGuiTreeNodeFlags.OpenOnArrow;
        if (children.Count == 0)
        {
            flags |= ImGuiTreeNodeFlags.Leaf;
        }
        
        if (!ImGui.TreeNodeEx($"[{root}] {boneName} {transform}###{root}", flags))
        {
            return;
        }
        
        foreach (var child in children)
        {
            DrawBoneTree(partial, pose, child);
        }
        
        ImGui.TreePop();
    }
}
