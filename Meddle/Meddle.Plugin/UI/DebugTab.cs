﻿using System.Numerics;
using System.Text.Json;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok.Animation.Rig;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services;
using Meddle.Plugin.Services.UI;
using Meddle.Plugin.Utils;

namespace Meddle.Plugin.UI;

public class DebugTab : ITab
{
    private readonly IClientState clientState;
    private readonly Configuration config;
    public MenuType MenuType => MenuType.Debug;
    private readonly SigUtil sigUtil;
    private readonly CommonUi commonUi;
    private readonly IGameGui gui;
    private readonly LayoutService layoutService;
    private readonly ParseService parseService;
    private readonly PbdHooks pbdHooks;
    private string boneSearch = "";

    private ICharacter? selectedCharacter;
    private enum BoneMode
    {
        Local,
        ModelPropagate,
        ModelNoPropagate,
        ModelRaw
    }
    
    private BoneMode boneMode = BoneMode.ModelPropagate;

    public DebugTab(Configuration config, SigUtil sigUtil, CommonUi commonUi, 
                    IGameGui gui, IClientState clientState, 
                    LayoutService layoutService,
                    ParseService parseService, PbdHooks pbdHooks)
    {
        this.config = config;
        this.sigUtil = sigUtil;
        this.commonUi = commonUi;
        this.gui = gui;
        this.clientState = clientState;
        this.layoutService = layoutService;
        this.parseService = parseService;
        this.pbdHooks = pbdHooks;
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }

    public string Name => "Debug";
    public int Order => 0;

    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true
    };
    
    public void Draw()
    {
        if (ImGui.CollapsingHeader("View Skeleton"))
        {
            DrawSelectedCharacter();
        }

        if (ImGui.CollapsingHeader("Config Json"))
        {
            var cofigJson = JsonSerializer.Serialize(config, jsonOptions);
            ImGui.TextWrapped(cofigJson);
        }

        if (ImGui.CollapsingHeader("Object Table"))
        {
            DrawObjectTable();
        }
        
        if (ImGui.CollapsingHeader("Addresses"))
        {
            DrawAddresses();
        }

        if (ImGui.CollapsingHeader("Cache Info"))
        {
            DrawCacheInfo();
        }

        if (ImGui.CollapsingHeader("PBD Info"))
        {
            using var table = ImRaii.Table("##PbdInfo", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable);
            ImGui.TableSetupColumn("Human", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("DeformerId", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("RaceSexId", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("PbdPath");
            ImGui.TableHeadersRow();
            foreach (var cachedDeformer in pbdHooks.GetDeformerCache())
            {
                foreach (var deformer in cachedDeformer.Value)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text($"{cachedDeformer.Key:X8}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{deformer.Key}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{deformer.Value.DeformerId}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{deformer.Value.RaceSexId}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{deformer.Value.PbdPath}");
                }
            }
        }
    }

    private unsafe void DrawObjectTable()
    {
        using var indent = ImRaii.PushIndent();
        var objectTable = sigUtil.GetGameObjectManager();
        for (int i = 0; i < objectTable->Objects.GameObjectIdSorted.Length; i++)
        {
            var objPtr = objectTable->Objects.GameObjectIdSorted[i];
            if (objPtr == null || objPtr.Value == null)
            {
                continue;
            }
            var obj = objPtr.Value;
            
            var kind = obj->GetObjectKind();
            if (ImGui.CollapsingHeader($"[{i}|{(nint)obj:X8}] {kind} - {obj->NameString}"))
            {
                UiUtil.Text($"Address: {(nint)obj:X8}", $"{(nint)obj:X8}");
                ImGui.Text($"Name: {obj->NameString}");
                ImGui.Text($"Type: {kind}");
                var drawObject = obj->DrawObject;
                if (drawObject != null)
                {
                    var drawObjectType = drawObject->GetObjectType();
                    ImGui.Text($"DrawObject Address: {(nint)drawObject:X8}");
                    ImGui.Text($"DrawObject Type: {drawObjectType}");
                    ImGui.Text($"DrawObject Position: {drawObject->Position}");
                    ImGui.Text($"DrawObject Rotation: {drawObject->Rotation}");
                    ImGui.Text($"DrawObject Scale: {drawObject->Scale}");
                    if (drawObjectType == ObjectType.CharacterBase)
                    {
                        using var cbaseIndent = ImRaii.PushIndent();
                        var cBase = (CharacterBase*)drawObject;
                        DrawCharacterBase(cBase, "CharacterBase");
                    }
                }
            }
        }
    }

    private unsafe void DrawAddresses()
    {
        var housingManager = sigUtil.GetHousingManager();
        var currentTerritory = housingManager->CurrentTerritory;
        var layoutWorld = sigUtil.GetLayoutWorld();
        var activeLayout = layoutWorld->ActiveLayout;
        
        UiUtil.Text($"HousingManager: {(nint)housingManager:X8}", $"{(nint)housingManager:X8}");
        UiUtil.Text($"CurrentTerritory: {(nint)currentTerritory:X8}", $"{(nint)currentTerritory:X8}");
        UiUtil.Text($"LayoutWorld: {(nint)layoutWorld:X8}", $"{(nint)layoutWorld:X8}");
        UiUtil.Text($"ActiveLayout: {(nint)activeLayout:X8}", $"{(nint)activeLayout:X8}");
    }

    private void DrawCacheInfo()
    {
        if (ImGui.Button("Clear Caches"))
        {
            parseService.ClearCaches();
        }
        
        using var table = ImRaii.Table("##CacheInfo", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        ImGui.TableSetupColumn("Type");
        ImGui.TableSetupColumn("Path");
        ImGui.TableHeadersRow();
        
        foreach (var (path, _) in parseService.ShpkCache)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Shpk");
            ImGui.TableNextColumn();
            ImGui.Text(path);
        }

        foreach (var (path, _) in parseService.MtrlCache)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Mtrl");
            ImGui.TableNextColumn();
            ImGui.Text(path);
        }
        
        foreach (var (path, _) in parseService.MdlCache)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Mdl");
            ImGui.TableNextColumn();
            ImGui.Text(path);
        }

        foreach (var (path, _) in parseService.TexCache)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Tex");
            ImGui.TableNextColumn();
            ImGui.Text(path);
        }
    }

    private unsafe void DrawSelectedCharacter()
    {
        using var indent = ImRaii.PushIndent();
        commonUi.DrawCharacterSelect(ref selectedCharacter);
        if (selectedCharacter == null)
        {
            ImGui.Text("No characters found");
            return;
        }

        // player address
        ImGui.Text($"Address: {selectedCharacter.Address:X8}");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Click to copy");
        }
        if (ImGui.IsItemClicked())
        {
            ImGui.SetClipboardText($"{selectedCharacter.Address:X8}");
        }


        var character = (Character*)selectedCharacter.Address;
        if (character == null)
        {
            ImGui.Text("Character is null");
            return;
        }

        var drawObject = character->DrawObject;
        if (drawObject == null)
        {
            ImGui.Text("DrawObject is null");
            return;
        }

        ImGui.Text($"DrawObject Address: {(nint)drawObject:X8}");

        var objectType = drawObject->GetObjectType();
        ImGui.Text($"Object Type: {objectType}");
        if (objectType != ObjectType.CharacterBase)
        {
            return;
        }

        var cBase = (CharacterBase*)drawObject;
        DrawCharacterBase(cBase, "Character");

        var ornament = character->OrnamentData.OrnamentObject;
        if (ornament != null)
        {
            var drawOrnament = ornament->DrawObject;
            if (drawOrnament != null)
            {
                ImGui.Text($"Ornament DrawObject Address: {(nint)drawOrnament:X8}");
                var ornamentType = drawOrnament->GetObjectType();
                ImGui.Text($"Ornament Object Type: {ornamentType}");
                if (ornamentType == ObjectType.CharacterBase)
                {
                    var ornamentBase = (CharacterBase*)drawOrnament;
                    DrawCharacterBase(ornamentBase, "Ornament");
                }
            }
        }
        
        var mount = character->Mount.MountObject;
        if (mount != null)
        {
            var drawMount = mount->DrawObject;
            if (drawMount != null)
            {
                ImGui.Text($"Mount DrawObject Address: {(nint)drawMount:X8}");
                var mountType = drawMount->GetObjectType();
                ImGui.Text($"Mount Object Type: {mountType}");
                if (mountType == ObjectType.CharacterBase)
                {
                    var mountBase = (CharacterBase*)drawMount;
                    DrawCharacterBase(mountBase, "Mount");
                }
            }
        }
        
        var companion = character->CompanionData.CompanionObject;
        if (companion != null)
        {
            var drawCompanion = companion->DrawObject;
            if (drawCompanion != null)
            {
                ImGui.Text($"Companion DrawObject Address: {(nint)drawCompanion:X8}");
                var companionType = drawCompanion->GetObjectType();
                ImGui.Text($"Companion Object Type: {companionType}");
                if (companionType == ObjectType.CharacterBase)
                {
                    var companionBase = (CharacterBase*)drawCompanion;
                    DrawCharacterBase(companionBase, "Companion");
                }
            }
        }

        var weapons = character->DrawData.WeaponData;
        for (int i = 0; i < weapons.Length; i++)
        {
            var weapon = weapons[i];
            if (weapon.DrawObject != null)
            {
                ImGui.Text($"Weapon {i} DrawObject Address: {(nint)weapon.DrawObject:X8}");
                var weaponType = weapon.DrawObject->GetObjectType();
                ImGui.Text($"Weapon {i} Object Type: {weaponType}");
                if (weaponType == ObjectType.CharacterBase)
                {
                    var weaponBase = (CharacterBase*)weapon.DrawObject;
                    DrawCharacterBase(weaponBase, $"Weapon {i}");
                }
            }
        }
    }

    private unsafe void DrawCharacterBase(CharacterBase* cBase, string name)
    {
        if (cBase == null)
        {
            ImGui.Text($"{name} CharacterBase is null");
            return;
        }
        using var id = ImRaii.PushId($"{(nint)cBase:X8}");
        if (ImGui.CollapsingHeader(name))
        {
            ImGui.Text($"Visible: {cBase->IsVisible}");
            ImGui.Text($"ModelType: {cBase->GetModelType()}");
            var skeleton = cBase->Skeleton;
            if (skeleton == null)
            {
                ImGui.Text($"{name} Skeleton is null");
                return;
            }

            ImGui.Text($"Skeleton: {(nint)cBase->Skeleton:X8}");
            ImGui.Text($"Partial Skeleton Count: {cBase->Skeleton->PartialSkeletonCount}");
            if (ImGui.CollapsingHeader("Draw Bones"))
            {
                // boneMode
                ImGui.Text("Bone Mode");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(200);
                using (var combo = ImRaii.Combo("##BoneMode", boneMode.ToString()))
                {
                    if (combo.Success)
                    {
                        foreach (BoneMode mode in Enum.GetValues(typeof(BoneMode)))
                        {
                            if (ImGui.Selectable(mode.ToString(), mode == boneMode))
                            {
                                boneMode = mode;
                            }
                        }
                    }
                }
                
                ImGui.Text("Bone Search");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(200);
                ImGui.InputText("##BoneSearch", ref boneSearch, 100);
                
                // imgui select partial skeleton by index
                for (int i = 0; i < skeleton->PartialSkeletonCount; i++)
                {
                    var partialSkeleton = skeleton->PartialSkeletons[i];
                    var handle = partialSkeleton.SkeletonResourceHandle;
                    if (handle == null) continue;
                    var path = handle->FileName.ParseString();
                    if (ImGui.CollapsingHeader($"Partial Skeleton {i}: {path}"))
                    {
                        var boneCount = StructExtensions.GetBoneCount(&partialSkeleton);
                        ImGui.Text($"Partial Skeleton Bone Count: {boneCount}");
                        using var boneTable = ImRaii.Table($"##BoneTable{i}", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable);
                        ImGui.TableSetupColumn("Bone", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Parent", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Translation", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Rotation", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Scale", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableHeadersRow();
                        DrawBoneTransformsOnScreen(partialSkeleton, boneMode);
                    }
                }
            }
        }
    }
    
    private unsafe void DrawBoneTransformsOnScreen(PartialSkeleton partialSkeleton, BoneMode boneMode)
    {
        var rootPos = partialSkeleton.Skeleton->Transform;
        var rootTransform = new Transform(rootPos);
        var pose = partialSkeleton.GetHavokPose(0);
        var boneCount = StructExtensions.GetBoneCount(&partialSkeleton);
        for (var i = 0; i < boneCount; i++)
        {
            var bone = pose->Skeleton->Bones[i];
            if (!string.IsNullOrEmpty(boneSearch) && bone.Name.String != null && !bone.Name.String.Contains(boneSearch))
            {
                continue;
            }
            
            var t = boneMode switch
            {
                BoneMode.Local => new Transform(pose->LocalPose[i]),
                BoneMode.ModelPropagate => new Transform(*pose->AccessBoneModelSpace(i, hkaPose.PropagateOrNot.Propagate)),
                BoneMode.ModelNoPropagate => new Transform(*pose->AccessBoneModelSpace(i, hkaPose.PropagateOrNot.DontPropagate)),
                BoneMode.ModelRaw => new Transform(pose->ModelPose[i]),
                _ => new Transform(pose->ModelPose[i])
            };

            var modelTransform = boneMode == BoneMode.Local ? new Transform(pose->ModelPose[i]) : t;
            var worldMatrix = modelTransform.AffineTransform.Matrix * rootTransform.AffineTransform.Matrix;
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text($"[{i}] {bone.Name.String}");
            var dotColorRgb = new Vector4(1, 1, 1, 0.5f);
            if (ImGui.IsItemHovered())
            {
                dotColorRgb = new Vector4(1, 0, 0, 0.5f);
            }
            
            ImGui.TableSetColumnIndex(1);
            var parentIndex = pose->Skeleton->ParentIndices[i];
            ImGui.Text(parentIndex.ToString());
            ImGui.TableSetColumnIndex(2);
            ImGui.Text($"{t.Translation:F3}");
            ImGui.TableSetColumnIndex(3);
            ImGui.Text($"X:{t.Rotation.X:F2} Y:{t.Rotation.Y:F2} " +
                       $"Z:{t.Rotation.Z:F2} W:{t.Rotation.W:F2}");
            ImGui.TableSetColumnIndex(4);
            ImGui.Text($"{t.Scale:F3}");
            
            if (gui.WorldToScreen(worldMatrix.Translation, out var screenPos))
            {
                var dotColor = ImGui.GetColorU32(dotColorRgb);
                ImGui.GetBackgroundDrawList().AddCircleFilled(screenPos, 5, dotColor);
            }
        }
    }
}
