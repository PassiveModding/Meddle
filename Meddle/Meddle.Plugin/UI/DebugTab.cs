using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Services;
using Meddle.Plugin.Services.UI;
using Meddle.Plugin.Utils;

namespace Meddle.Plugin.UI;

public class DebugTab : ITab
{
    private readonly IClientState clientState;
    private readonly Configuration config;
    private readonly SigUtil sigUtil;
    private readonly CommonUi commonUi;
    private readonly IGameGui gui;
    private readonly IObjectTable objectTable;
    private readonly ParseService parseService;
    private readonly PbdHooks pbdHooks;

    private ICharacter? selectedCharacter;

    public DebugTab(Configuration config, SigUtil sigUtil, CommonUi commonUi, 
                    IGameGui gui, IClientState clientState, IObjectTable objectTable, ParseService parseService, PbdHooks pbdHooks)
    {
        this.config = config;
        this.sigUtil = sigUtil;
        this.commonUi = commonUi;
        this.gui = gui;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.parseService = parseService;
        this.pbdHooks = pbdHooks;
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }

    public string Name => "Debug";
    public int Order => int.MaxValue - 10;
    public bool DisplayTab => config.ShowDebug;

    public void Draw()
    {
        if (ImGui.CollapsingHeader("View Skeleton"))
        {
            DrawSelectedCharacter();
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

    private void DrawObjectTable()
    {
        using var indent = ImRaii.PushIndent();
        for (int i = 0; i < objectTable.Length; i++)
        {
            var obj = objectTable[i];
            if (obj == null)
            {
                continue;
            }
            
            if (ImGui.CollapsingHeader($"[{i}] {obj.ObjectKind} - {obj.Name.TextValue}"))
            {
                ImGui.Text($"Index: {i}");
                ImGui.Text($"Address: {obj.Address:X8}");
                ImGui.Text($"Name: {obj.Name.TextValue}");
                ImGui.Text($"Type: {obj.ObjectKind}");
                ImGui.Text($"Position: {obj.Position}");
                ImGui.Text($"Rotation: {obj.Rotation}");
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
                        DrawBoneTransformsOnScreen(partialSkeleton);
                    }
                }
            }
        }
    }
    
    private unsafe void DrawBoneTransformsOnScreen(PartialSkeleton partialSkeleton)
    {
        var rootPos = partialSkeleton.Skeleton->Transform;
        var rootTransform = new Transform(rootPos);
        var pose = partialSkeleton.GetHavokPose(0);
        var boneCount = StructExtensions.GetBoneCount(&partialSkeleton);
        for (var i = 0; i < boneCount; i++)
        {
            var bone = pose->Skeleton->Bones[i];
            var modelTransform = new Transform(pose->ModelPose[i]);
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
            ImGui.Text($"{modelTransform.Translation:F3}");
            ImGui.TableSetColumnIndex(3);
            ImGui.Text($"X:{modelTransform.Rotation.X:F2} Y:{modelTransform.Rotation.Y:F2} " +
                       $"Z:{modelTransform.Rotation.Z:F2} W:{modelTransform.Rotation.W:F2}");
            ImGui.TableSetColumnIndex(4);
            ImGui.Text($"{modelTransform.Scale:F3}");
            
            if (gui.WorldToScreen(worldMatrix.Translation, out var screenPos))
            {
                var dotColor = ImGui.GetColorU32(dotColorRgb);
                ImGui.GetBackgroundDrawList().AddCircleFilled(screenPos, 5, dotColor);
            }
        }
    }
}
