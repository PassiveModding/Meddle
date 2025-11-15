using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using Dalamud.Bindings.ImGui;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Composer;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.UI.Layout;
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
    
    public static void DrawProgress(Task exportTask, ProgressWrapper? progressWrapper, CancellationTokenSource cancelToken)
    {
        if (exportTask.IsFaulted)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Export failed");
            ImGui.TextWrapped(exportTask.Exception?.ToString());
        }
        
        if (exportTask.IsCompleted) return;
        
        ImGui.Text("Export in progress..."); 
        ImGui.SameLine();
        using (var disabled = ImRaii.Disabled(cancelToken.IsCancellationRequested))
        {
            if (ImGui.Button(cancelToken.IsCancellationRequested ? "Cancelling..." : "Cancel"))
            {
                cancelToken.Cancel();
            }
        }
        
        if (progressWrapper is {Progress: not null})
        {
            DrawProgressRecursive(progressWrapper.Progress);
        }

        return;

        void DrawProgressRecursive(ExportProgress rProgress)
        {
            if (rProgress.IsComplete) return;
            ImGui.Text($"{(rProgress.Name != null ? $"{rProgress.Name} " : null)}Exporting {rProgress.Progress} of {rProgress.Total}");
            ImGui.ProgressBar(rProgress.Progress / (float)rProgress.Total, new Vector2(-1, 0), rProgress.Name ?? "");
            if (rProgress.Children.Count > 0)
            {
                using var indent = ImRaii.PushIndent();
                foreach (var child in rProgress.Children)
                {
                    if (child == rProgress)
                    {
                        ImGui.Text("Recursive progress detected, skipping");
                        continue;
                    }
                    DrawProgressRecursive(child);
                }
            }
        }
    }


    [Flags]
    public enum ExportConfigDrawFlags
    {
        None = 0,
        ShowExportPose = 1,
        ShowBgPartOptions = 2,
        ShowUseDeformer = 4,
        ShowSubmeshOptions = 8,
        ShowTerrainOptions = 16,
    }

    public static bool DrawExportConfig(Configuration.ExportConfiguration exportConfiguration, ExportConfigDrawFlags flags = ExportConfigDrawFlags.None)
    {
        bool changed = false;
        var cacheFileTypes = exportConfiguration.CacheFileTypes;
        if (EnumExtensions.DrawEnumCombo("Extra Cache Files", ref cacheFileTypes))
        {
            exportConfiguration.CacheFileTypes = cacheFileTypes;
            changed = true;
        }

        ImGui.SameLine();
        HintCircle("Select which files to cache when exporting, this is not needed in most cases");

        var exportType = exportConfiguration.ExportType;
        if (EnumExtensions.DrawEnumCombo("Export type", ref exportType))
        {
            exportConfiguration.ExportType = exportType;
            if (exportType == 0)
            {
                exportConfiguration.ExportType = Configuration.DefaultExportType;
            }

            changed = true;
        }
        
        ImGui.SameLine();
        HintCircle("Select the type of export to use, GLTF is recommended for most cases.\n" +
                   "GLB is a binary version of GLTF, and OBJ is a legacy format that is not recommended for most cases.");

        if (exportType.HasFlag(ExportType.OBJ))
        {
            // draw warning that OBJ is not recommended
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "OBJ export is not recommended and may not work as expected.");
        }

        if (flags.HasFlag(ExportConfigDrawFlags.ShowExportPose))
        {
            var poseMode = exportConfiguration.PoseMode;
            if (EnumExtensions.DrawEnumDropDown("Pose Mode", ref poseMode))
            {
                exportConfiguration.PoseMode = poseMode;
                changed = true;
            }
            
            ImGui.SameLine();
            HintCircle($"Reference Pose ({nameof(SkeletonUtils.PoseMode.None)}): Export will not include a pose track.\n" +
                       $"Reference Pose with Scale ({nameof(SkeletonUtils.PoseMode.LocalScaleOnly)}): Export will include only scaling on the pose track.\n" +
                       $"[default]Pose ({nameof(SkeletonUtils.PoseMode.Local)}): Export will include scaling, rotation and translation on the pose track.");
        }
        
        if (flags.HasFlag(ExportConfigDrawFlags.ShowUseDeformer))
        {
            var useDeformer = exportConfiguration.UseDeformer;
            if (ImGui.Checkbox("Use Deformer", ref useDeformer))
            {
                exportConfiguration.UseDeformer = useDeformer;
                changed = true;
            }
        
            ImGui.SameLine();
            HintCircle("If enabled, the export will use the deformer to export the model.\n" +
                       "This is recommended for most models, but will result in different deformations based on the race associated with the model.");
        }

        if (flags.HasFlag(ExportConfigDrawFlags.ShowBgPartOptions))
        {
            var skipHiddenBgParts = exportConfiguration.SkipHiddenBgParts;
            if (ImGui.Checkbox("Remove hidden background parts", ref skipHiddenBgParts))
            {
                exportConfiguration.SkipHiddenBgParts = skipHiddenBgParts;
                changed = true;
            }
        
            ImGui.SameLine();
            HintCircle("If enabled, the export will skip any models that are not visible in the game.\n" +
                       "Example: if an arena changes shape throughout an encounter, the export will only include the arena that is currently visible.");
        }

        if (flags.HasFlag(ExportConfigDrawFlags.ShowSubmeshOptions))
        {
            var removeAttributeDisabledSubmeshes = exportConfiguration.RemoveAttributeDisabledSubmeshes;
            if (ImGui.Checkbox("Remove unused character features", ref removeAttributeDisabledSubmeshes))
            {
                exportConfiguration.RemoveAttributeDisabledSubmeshes = removeAttributeDisabledSubmeshes;
                changed = true;
            }

            ImGui.SameLine();
            HintCircle("Certain character features can be toggled on and off in the character creator,\n" +
                       "this will make sure the export only includes the features that are currently enabled.");
        }
        
        if (flags.HasFlag(ExportConfigDrawFlags.ShowTerrainOptions))
        {
            var limitTerrainDistance = exportConfiguration.TerrainExportDistance;
            if (ImGui.DragFloat("##TerrainExportDistance", ref limitTerrainDistance, 0.1f, 0f, 10000f))
            {
                exportConfiguration.TerrainExportDistance = limitTerrainDistance;
                changed = true;
            }
            
            ImGui.SameLine();
            
            var limitTerrainExportRange = exportConfiguration.LimitTerrainExportRange;
            if (ImGui.Checkbox("Limit terrain range", ref limitTerrainExportRange))
            {
                exportConfiguration.LimitTerrainExportRange = limitTerrainExportRange;
                changed = true;
            }
            
            ImGui.SameLine();
            
            HintCircle("If enabled, the export will only include terrain within the specified distance from the player.\n" +
                       "This is useful for reducing the size of the export, but may result in missing terrain in some areas.");
        }

        // var rootAttachHandling = exportConfiguration.RootAttachHandling;
        // if (EnumExtensions.DrawEnumDropDown("Root Attach Handling", ref rootAttachHandling))
        // {
        //     exportConfiguration.RootAttachHandling = rootAttachHandling;
        //     changed = true;
        // }
        //
        // ImGui.SameLine();
        // HintCircle("PlayerAsAttachChild: If a 'Character' has a root attach (typically a mount), export the player as a child of the root attach\n" +
        //             "Exclude: Export the root attach separately from the player");

        var enableWindingFlip = exportConfiguration.EnableWindingFlip;
        if (ImGui.Checkbox("Enable Winding Order Flip", ref enableWindingFlip))
        {
            exportConfiguration.EnableWindingFlip = enableWindingFlip;
            changed = true;
        }

        ImGui.SameLine();
        HintCircle("Automatically flips triangle winding order when face normals don't match vertex normals.\n" +
                   "Enable this if exported models have inverted or incorrect face normals.");

        return changed;
    }

    public static void HintCircle(string text)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.Text(FontAwesomeIcon.QuestionCircle.ToIconString());
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text(text);
            ImGui.EndTooltip();
        }
    }

    public static void DrawCustomizeParams(CustomizeParameter? customize)
    {
        if (customize == null)
        {
            ImGui.Text("No CustomizeParameter");
            return;
        }
        ImGui.ColorEdit3("Skin Color", ref customize.SkinColor, ImGuiColorEditFlags.NoInputs);
        ImGui.ColorEdit4("Lip Color", ref customize.LipColor, ImGuiColorEditFlags.NoInputs);
        ImGui.ColorEdit3("Main Color", ref customize.MainColor, ImGuiColorEditFlags.NoInputs);
        ImGui.ColorEdit3("Mesh Color", ref customize.MeshColor, ImGuiColorEditFlags.NoInputs);
        ImGui.ColorEdit4("Left Color", ref customize.LeftColor, ImGuiColorEditFlags.NoInputs);
        ImGui.ColorEdit4("Right Color", ref customize.RightColor, ImGuiColorEditFlags.NoInputs);
        ImGui.ColorEdit3("Option Color", ref customize.OptionColor, ImGuiColorEditFlags.NoInputs);
        ImGui.ColorEdit4("DecalColor", ref customize.DecalColor, ImGuiColorEditFlags.NoInputs);
        using var width = ImRaii.ItemWidth(100);
        ImGui.InputFloat("Face Paint UV Offset", ref customize.FacePaintUvOffset, flags: ImGuiInputTextFlags.ReadOnly);
        ImGui.InputFloat("Face Paint UV Multiplier", ref customize.FacePaintUvMultiplier, flags: ImGuiInputTextFlags.ReadOnly);
    }
    
    public static void DrawCustomizeData(CustomizeData? customize)
    {
        if (customize == null)
        {
            ImGui.Text("No CustomizeData");
            return;
        }
        ImGui.Text($"Lipstick: {customize.LipStick}");
        ImGui.Text($"Highlights: {customize.Highlights}");
        ImGui.Text($"FacePaintReversed: {customize.FacePaintReversed}");
    }

    public static void DrawColorTable(IColorTableSet table)
    {
        if (table is ColorTableSet set)
        {
            DrawColorTable(set.ColorTable, set.ColorDyeTable);
        }
        else
        {
            ImGui.Text("Unsupported ColorTableSet");
        }
    }

    public static void DrawColorTable(ColorTable table, ColorDyeTable? dyeTable = null)
    {
        DrawColorTable(table.Rows, dyeTable);
    }

    private static readonly (string ColumnName, int ColumnWidth, ImGuiTableColumnFlags ColumnFlags, Action<int, ColorTableRow, ColorDyeTableRow?> Draw)[] ColumnDefs =
    [
        ("Row", 50, ImGuiTableColumnFlags.WidthFixed, (i, _, _) =>
            {
                ImGui.Text($"{i}");
            }),
        ("Diffuse", 50, ImGuiTableColumnFlags.WidthFixed, (_, row, _) =>
            {
                ImGui.ColorButton("##rowdiff", new Vector4(row.Diffuse, 1f), ImGuiColorEditFlags.NoAlpha);
            }),
        ("Specular", 50, ImGuiTableColumnFlags.WidthFixed, (_, row, _) =>
            {
                ImGui.ColorButton("##rowspec", new Vector4(row.Specular, 1f), ImGuiColorEditFlags.NoAlpha);
            }),
        ("Emissive", 50, ImGuiTableColumnFlags.WidthFixed, (_, row, _) =>
            {
                ImGui.ColorButton("##rowemm", new Vector4(row.Emissive, 1f), ImGuiColorEditFlags.NoAlpha);
            }),
        ("Sheen Rate", 50, ImGuiTableColumnFlags.WidthFixed, (_, row, _) =>
            {
                ImGui.Text($"{row.SheenRate:0.##}");
            }),
        ("Sheen Tint", 50, ImGuiTableColumnFlags.WidthFixed, (_, row, _) =>
            {
                ImGui.Text($"{row.SheenTint:0.##}");
            }),
        ("Sheen Apt.", 50, ImGuiTableColumnFlags.WidthFixed, (_, row, _) =>
            {
                ImGui.Text($"{row.SheenAptitude:0.##}");
            }),
        ("Roughness", 50, ImGuiTableColumnFlags.WidthFixed, (_, row, _) =>
            {
                ImGui.Text($"{row.Roughness:0.##}");
            }),
        ("Metalness", 50, ImGuiTableColumnFlags.WidthFixed, (_, row, _) =>
            {
                ImGui.Text($"{row.Metalness:0.##}");
            }),
        ("Anisotropy", 50, ImGuiTableColumnFlags.WidthFixed, (_, row, _) =>
            {
                ImGui.Text($"{row.Anisotropy:0.##}");
            }),
        ("Sphere Mask", 50, ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultHide, (_, row, _) =>
            {
                ImGui.Text($"{row.SphereMask:0.##}");
            }),
        ("Sphere Idx", 50, ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultHide, (_, row, _) =>
            {
                ImGui.Text($"{row.SphereIndex}");
            }),
        ("Shader Idx", 50, ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultHide, (_, row, _) =>
            {
                ImGui.Text($"{row.ShaderId}");
            }),
        ("(L)Gloss", 50, ImGuiTableColumnFlags.WidthFixed, (_, row, _) =>
            {
                ImGui.Text($"{row.GlossStrength:0.##}");
            }),
        ("(L)Spec Str", 50, ImGuiTableColumnFlags.WidthFixed, (_, row, _) =>
            {
                ImGui.Text($"{row.SpecularStrength:0.##}");
            }),
        ("Tile Matrix", 100, ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultHide, (_, row, _) =>
            {
                ImGui.Text($"UU: {row.TileMatrix.UU:0.##}, UV: {row.TileMatrix.UV:0.##}, VU: {row.TileMatrix.VU:0.##}, VV: {row.TileMatrix.VV:0.##}");
            }),
        ("Tile Idx", 50, ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultHide, (_, row, _) =>
            {
                ImGui.Text($"{row.TileIndex}");
            }),
        ("Tile Alpha", 50, ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultHide, (_, row, _) =>
            {
                ImGui.Text($"{row.TileAlpha:0.##}");
            }),
    ];

    public static void DrawColorTable(ReadOnlySpan<ColorTableRow> tableRows, ColorDyeTable? dyeTable = null)
    {
        using var mainTable = ImRaii.Table("ColorTable", ColumnDefs.Length, ImGuiTableFlags.Resizable |
                                                                            ImGuiTableFlags.Reorderable | ImGuiTableFlags.Hideable);

        foreach (var (name, width, flags, _) in ColumnDefs)
        {
            ImGui.TableSetupColumn(name, flags, width);
        }

        ImGui.TableHeadersRow();

        for (var i = 0; i < tableRows.Length; i++)
        {
            var row = tableRows[i];
            var dye = dyeTable?.Rows[i];
            ImGui.TableNextRow();
            for (var j = 0; j < ColumnDefs.Length; j++)
            {
                ImGui.TableSetColumnIndex(j);
                ColumnDefs[j].Draw(i, row, dye);
            }
        }
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

        var attach = characterPointer.Value->Attach;
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

    // public static Vector4 ConvertU32ColorToVector4(uint color)
    // {
    //     var r = (color & 0xFF) / 255f;
    //     var g = ((color >> 8) & 0xFF) / 255f;
    //     var b = ((color >> 16) & 0xFF) / 255f;
    //     var a = ((color >> 24) & 0xFF) / 255f;
    //     return new Vector4(r, g, b, a);
    // }

    /// <summary> Square stores its colors as BGR values so R and B need to be shuffled and Alpha set to max. </summary>
    public static uint SeColorToRgba(uint color)
        => ((color & 0xFF) << 16) | ((color >> 16) & 0xFF) | (color & 0xFF00) | 0xFF000000;

    public static string GetCharacterName(this string? name, Configuration config, ObjectKind kind, string? suffix = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return $"Unknown{suffix}";
        }
        
        if (!string.IsNullOrWhiteSpace(config.PlayerNameOverride) && kind == ObjectKind.Pc)
        {
            return $"{config.PlayerNameOverride}{suffix}";
        }

        return name;
    }
}
