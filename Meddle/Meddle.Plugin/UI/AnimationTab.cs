using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Meddle.Utils.Skeletons;
using Microsoft.Extensions.Logging;
using SharpGLTF.Transforms;
using Attach = Meddle.Plugin.Skeleton.Attach;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;

namespace Meddle.Plugin.UI;

public class AnimationTab : ITab
{
    private readonly IFramework framework;
    private readonly ILogger<AnimationTab> logger;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly ExportUtil exportUtil;
    private readonly PluginState pluginState;
    private readonly Configuration config;
    public string Name => "Animation";
    public int Order => 2;
    public bool DisplayTab => true;
    private bool captureAnimation;
    private ICharacter? selectedCharacter;
    private readonly List<AnimationFrameData> frames = new();
    private bool includePositionalData;
    
    public AnimationTab(IFramework framework, ILogger<AnimationTab> logger, 
                        IClientState clientState, 
                        IObjectTable objectTable,
                        ExportUtil exportUtil,
                        PluginState pluginState,
                        Configuration config)
    {
        this.framework = framework;
        this.logger = logger;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.exportUtil = exportUtil;
        this.pluginState = pluginState;
        this.config = config;
        this.framework.Update += OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework1)
    {
        if (!pluginState.InteropResolved)
        {
            return;
        }
        Capture();
    }

    public unsafe void Draw()
    {
        if (!pluginState.InteropResolved)
        {
            ImGui.Text("Waiting for Interop to resolve...");
            return;
        }
        
        // Warning text:
        ImGui.TextWrapped("NOTE: Animation exports are experimental, held weapons, mounts and other attached objects may not work as expected.");
        
        ICharacter[] objects;
        if (clientState.LocalPlayer != null)
        {
            objects = objectTable.OfType<ICharacter>()
                                 .Where(obj => obj.IsValid() && obj.IsValidCharacterBase())
                                 .OrderBy(c => clientState.GetDistanceToLocalPlayer(c).LengthSquared())
                                 .ToArray();
        }
        else
        {
            // login/char creator produces "invalid" characters but are still usable I guess
            objects = objectTable.OfType<ICharacter>()
                                 .Where(obj => obj.IsValidHuman())
                                 .OrderBy(c => clientState.GetDistanceToLocalPlayer(c).LengthSquared())
                                 .ToArray();
        }

        selectedCharacter ??= objects.FirstOrDefault() ?? clientState.LocalPlayer;
        
        ImGui.Text("Select Character");
        var preview = selectedCharacter != null ? clientState.GetCharacterDisplayText(selectedCharacter, config.PlayerNameOverride) : "None";
        using (var combo = ImRaii.Combo("##Character", preview))
        {
            if (combo)
            {
                foreach (var character in objects)
                {
                    if (ImGui.Selectable(clientState.GetCharacterDisplayText(character, config.PlayerNameOverride)))
                    {
                        selectedCharacter = character;
                    }
                }
            }
        }
        
        if (selectedCharacter == null) return;
        if (ImGui.Checkbox("Capture Animation", ref captureAnimation))
        {
            if (captureAnimation)
            {
                logger.LogInformation("Capturing animation");
            }
            else
            {
                logger.LogInformation("Stopped capturing animation");
            }
        }
        
        var frameCount = frames.Count;
        ImGui.Text($"Frames: {frameCount}");
        if (ImGui.Button("Export"))
        {
            exportUtil.ExportAnimation(frames, includePositionalData);
        }
        
        ImGui.SameLine();
        ImGui.Checkbox("Include Positional Data", ref includePositionalData);
        
        if (ImGui.Button("Clear"))
        {
            frames.Clear();
        }
        
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Frames"))
        {
            // render frames
            foreach (var frame in frames.ToArray())
            {
                if (ImGui.CollapsingHeader($"Frame: {frame.Time}##{frame.GetHashCode()}"))
                {
                    using var frameIndent = ImRaii.PushIndent();
                    foreach (var partial in frame.Skeleton.PartialSkeletons)
                    {
                        if (ImGui.CollapsingHeader($"Partial: {partial.HandlePath}##{partial.GetHashCode()}"))
                        {
                            ImGui.Text($"Connected Bone Index: {partial.ConnectedBoneIndex}");
                            var poseData = partial.Poses.FirstOrDefault();
                            if (poseData == null) continue;
                            for (int i = 0; i < poseData.Pose.Count; i++)
                            {
                                var transform = poseData.Pose[i];
                                var boneName = partial.HkSkeleton?.BoneNames[i] ?? "Bone";
                                ImGui.Text($"[{i}]{boneName} " +
                                           $"Scale: {transform.Scale} " +
                                           $"Rotation: {transform.Rotation} " +
                                           $"Translation: {transform.Translation}");
                            }
                        }
                    }
                }
            }
        }
        
        if (ImGui.CollapsingHeader("Skeleton"))
        {
            DrawSelectedCharacter();
        }
    }

    private unsafe void Capture()
    {
        if (!captureAnimation) return;
        if (selectedCharacter == null) return;
        
        // 60fps
        if (frames.Count > 0 && DateTime.UtcNow - frames[^1].Time < TimeSpan.FromMilliseconds(100))
        {
            return;
        }
        
        var charPtr = (Character*)selectedCharacter.Address;
        if (charPtr == null)
        {
            logger.LogWarning("Character is null");
            captureAnimation = false;
            return;
        }
        var cBase = (CharacterBase*)charPtr->GameObject.DrawObject;
        if (cBase == null)
        {
            logger.LogWarning("CharacterBase is null");
            captureAnimation = false;
            return;
        }

        var skeleton = cBase->Skeleton;
        if (skeleton == null)
        {
            logger.LogWarning("Skeleton is null");
            captureAnimation = false;
            return;
        }

        var mSkele = new Skeleton.Skeleton(skeleton);
        var position = cBase->Position;
        var rotation = cBase->Rotation;
        var scale = cBase->Scale;
        var transform = new AffineTransform(scale, rotation, position);
        
        var attachments = new List<AttachedSkeleton>();
        
        var ornament = charPtr->OrnamentData.OrnamentObject;
        var companion = charPtr->CompanionData.CompanionObject;
        var mount = charPtr->Mount.MountObject;
        var weaponData = charPtr->DrawData.WeaponData;
        
        if (ornament != null && ornament->DrawObject != null && ornament->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var ornamentBase = (CharacterBase*)ornament->DrawObject;
            attachments.Add(new AttachedSkeleton($"{(nint)ornamentBase:X8}", new Skeleton.Skeleton(ornamentBase->Skeleton), new Attach(ornamentBase->Attach)));
        }
        
        if (companion != null && companion->DrawObject != null && companion->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var companionBase = (CharacterBase*)companion->DrawObject;
            attachments.Add(new AttachedSkeleton($"{(nint)companionBase:X8}", new Skeleton.Skeleton(companionBase->Skeleton), new Attach(companionBase->Attach)));
        }
        
        if (mount != null && mount->DrawObject != null && mount->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var mountBase = (CharacterBase*)mount->DrawObject;
            attachments.Add(new AttachedSkeleton($"{(nint)mountBase:X8}", new Skeleton.Skeleton(mountBase->Skeleton), new Attach(mountBase->Attach)));
        }
        
        if (weaponData != null)
        {
            for (var i = 0; i < weaponData.Length; ++i)
            {
                var weapon = weaponData[i];
                if (weapon.DrawObject != null && weapon.DrawObject->GetObjectType() == ObjectType.CharacterBase)
                {
                    var weaponBase = (CharacterBase*)weapon.DrawObject;
                    attachments.Add(new AttachedSkeleton($"{(nint)weaponBase:X8}", new Skeleton.Skeleton(weaponBase->Skeleton), new Attach(weaponBase->Attach)));
                }
            }
        }
        
        frames.Add(new AnimationFrameData(DateTime.UtcNow, mSkele, transform, attachments.ToArray()));
    }

    private void DrawSkeleton(Skeleton.Skeleton skeleton)
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
    
    private unsafe void DrawSkeleton(FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton* sk, string context)
    {
        using var skeletonIndent = ImRaii.PushIndent();
        using var skeletonId = ImRaii.PushId($"{(nint)sk:X8}");
        ImGui.Text($"Partial Skeletons: {sk->PartialSkeletonCount}");
        ImGui.Text($"Transform: {new Transform(sk->Transform)}");
        var mainPose = GetPose(sk);
        
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
                        if (mainPose != null && mainPose.TryGetValue(boneName, out var transform))
                        {
                            ImGui.SameLine();
                            ImGui.Text($" {new Transform(transform)}");
                        }
                    }
                }
            }
        }
    }

    private unsafe void DrawSelectedCharacter()
    {
        if (selectedCharacter == null) return;
        var charPtr = (Character*)selectedCharacter.Address;
        if (charPtr == null) return;
        var cBase = (CharacterBase*)charPtr->GameObject.DrawObject;
        if (cBase == null) return;
        
        DrawCharacterAttaches(charPtr);
    }

    private unsafe void DrawCharacterAttaches(Character* charPtr)
    {
        var cBase = (CharacterBase*)charPtr->GameObject.DrawObject;


        DrawCharacterBase(cBase, "Main");
        DrawOrnamentContainer(charPtr->OrnamentData);
        DrawCompanionContainer(charPtr->CompanionData);
        DrawMountContainer(charPtr->Mount);
        DrawDrawDataContainer(charPtr->DrawData);
    }
    
    private unsafe void DrawDrawDataContainer(DrawDataContainer drawDataContainer)
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
    
    private unsafe void DrawCompanionContainer(CompanionContainer companionContainer)
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
    
    private unsafe void DrawMountContainer(MountContainer mountContainer)
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

    private unsafe void DrawOrnamentContainer(OrnamentContainer ornamentContainer)
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

    private unsafe void DrawCharacterBase(CharacterBase* character, string name)
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
        
        var pose = GetPose(skeleton);
        if (pose == null)
        {
            ImGui.Text("No pose data");
            return;
        }

        var modelType = character->GetModelType();
        var attachHeader = $"[{modelType}]{name} Attach Pose ({attachPoint.ExecuteType},{attachPoint.AttachmentCount})";
        if (character->Attach.ExecuteType > 3)
        {
            var attachedPartialSkeleton = attachPoint.OwnerSkeleton!.PartialSkeletons[attachPoint.PartialSkeletonIdx];
            var boneName = attachedPartialSkeleton.HkSkeleton!.BoneNames[attachPoint.BoneIdx];
            attachHeader += $" at {boneName}";
        }
        else if (character->Attach.ExecuteType == 3)
        {
            var attachedPartialSkeleton = attachPoint.OwnerSkeleton!.PartialSkeletons[attachPoint.PartialSkeletonIdx];
            if (attachedPartialSkeleton.HkSkeleton != null && attachPoint.BoneIdx < attachedPartialSkeleton.HkSkeleton.BoneNames.Count)
            {
                var boneName = attachedPartialSkeleton.HkSkeleton.BoneNames[attachPoint.BoneIdx];
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

    private unsafe void DrawAttachInfo(CharacterBase* character, Attach attachPoint)
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
        DrawModels(character);
    }

    private unsafe void DrawModels(CharacterBase* character)
    {
        var models = character->ModelsSpan;
        foreach (var model in models)
        {
            if (model == null)
                continue;

            var boneCount = model.Value->BoneCount;
            ImGui.Text($"Model at: {model.Value->SlotIndex} Bone Count: {boneCount}");
        }
    }
    
    private static unsafe Dictionary<string, hkQsTransformf>? GetPose(FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton* skeleton)
    {
        if (skeleton == null)
            return null;
        
        var ret = new Dictionary<string, hkQsTransformf>();
        
        for(var i = 0; i < skeleton->PartialSkeletonCount; ++i)
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

                ret[boneName] = pose->LocalPose[j];
            }
        }

        return ret;
    }
    
    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
    }
}
