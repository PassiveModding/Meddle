using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Utils;
using Meddle.Utils.Skeletons;
using Microsoft.Extensions.Logging;
using SharpGLTF.Transforms;
using Attach = Meddle.Plugin.Skeleton.Attach;

namespace Meddle.Plugin.UI;

public class AnimationTab : ITab
{
    private readonly IFramework framework;
    private readonly ILogger<AnimationTab> logger;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly ExportUtil exportUtil;
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
                        Configuration config)
    {
        this.framework = framework;
        this.logger = logger;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.exportUtil = exportUtil;
        this.config = config;
        this.framework.Update += OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework1)
    {
        Capture();
    }

    public unsafe void Draw()
    {
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
                    foreach (var partial in frame.Skeleton.PartialSkeletons)
                    {
                        ImGui.Text($"Partial: {partial.HandlePath}");
                        ImGui.Text($"Connected Bone Index: {partial.ConnectedBoneIndex}");
                        var poseData = partial.Poses.FirstOrDefault();
                        if (poseData == null) continue;
                        for (int i = 0; i < poseData.Pose.Count; i++)
                        {
                            var transform = poseData.Pose[i];
                            ImGui.Text(
                                $"Bone: {i} Scale: {transform.Scale} Rotation: {transform.Rotation} Translation: {transform.Translation}");
                        }
                    }
                }
            }
        }
        
        if (ImGui.CollapsingHeader("Skeleton"))
        {
            DrawSkeleton();
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

    private unsafe void DrawSkeleton(FFXIVClientStructs.FFXIV.Client.Graphics.Render.Skeleton* sk)
    {            
        ImGui.Indent();
        try
        {
            var mainPose = GetPose(sk);
            ImGui.Text($"{(nint)sk:X8}");
            for (var i = 0; i < sk->PartialSkeletonCount; ++i)
            {
                ImGui.PushID($"PartialSkeleton_{i}");
                var handle = sk->PartialSkeletons[i].SkeletonResourceHandle;
                string partialName;
                if (handle == null)
                {
                    partialName = $"Partial {i}";
                }
                else
                {
                    partialName = $"Partial {i}: {handle->FileName.ToString()}";
                }
                
                if (ImGui.CollapsingHeader(partialName))
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

                ImGui.PopID();
                ImGui.Separator();
            }
        } 
        finally
        {
            ImGui.Unindent();
        }

    }
    
    private unsafe void DrawSkeleton()
    {
        if (selectedCharacter == null) return;
        var charPtr = (Character*)selectedCharacter.Address;
        if (charPtr == null) return;
        var cBase = (CharacterBase*)charPtr->GameObject.DrawObject;
        if (cBase == null) return;
        var skeleton = cBase->Skeleton;
        if (skeleton == null) return;


        if (ImGui.CollapsingHeader("Main Pose"))
        {
            DrawSkeleton(skeleton);
        }

        var ornament = charPtr->OrnamentData.OrnamentObject;
        var companion = charPtr->CompanionData.CompanionObject;
        var mount = charPtr->Mount.MountObject;
        var weaponData = charPtr->DrawData.WeaponData;
        
        if (ornament != null && ornament->DrawObject != null && ornament->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var ornamentBase = (CharacterBase*)ornament->DrawObject;
            DrawAttach(ornamentBase, cBase, "Ornament");
        }
        
        if (companion != null && companion->DrawObject != null && companion->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var companionBase = (CharacterBase*)companion->DrawObject;
            DrawAttach(companionBase, cBase, "Companion");
        }
        
        if (mount != null && mount->DrawObject != null && mount->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var mountBase = (CharacterBase*)mount->DrawObject;
            DrawAttach(mountBase, cBase, "Mount");
        }
        
        if (weaponData != null)
        {
            for (var i = 0; i < weaponData.Length; ++i)
            {
                var weapon = weaponData[i];
                if (weapon.DrawObject != null && weapon.DrawObject->GetObjectType() == ObjectType.CharacterBase)
                {
                    var weaponBase = (CharacterBase*)weapon.DrawObject;
                    DrawAttach(weaponBase, cBase, $"Weapon {i}");
                }
            }
        }
    }

    private unsafe void DrawAttach(CharacterBase* attach, CharacterBase* parent, string name)
    {
        var skeleton = attach->Skeleton;
        if (skeleton == null)
            return;
        
        ImGui.PushID((nint)attach);
        try
        {
            var attachPoint = new Attach(attach->Attach);
            var pose = GetPose(skeleton);
            var attachBone = parent->Skeleton->PartialSkeletons[attachPoint.PartialSkeletonIdx];
            var hkaPose = attachBone.GetHavokPose(0);
            var boneName = hkaPose->Skeleton->Bones[attachPoint.BoneIdx].Name.String;
            if (ImGui.CollapsingHeader($"{name} Attach Pose {name} at {boneName}") && pose != null)
            {
                var position = attach->Position;
                var rotation = attach->Rotation;
                var scale = attach->Scale;
                var aTransform = new AffineTransform(scale, rotation, position);
                var transform = new Transform(aTransform);
                ImGui.Text($"Transform: {transform}");
                ImGui.Text($"Root: {attachPoint.OffsetTransform}");
                DrawSkeleton(skeleton);
            }

        } 
        finally
        {
            ImGui.PopID();
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
