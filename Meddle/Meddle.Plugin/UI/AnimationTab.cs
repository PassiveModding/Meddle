using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services;
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
    private readonly PluginState pluginState;
    private readonly Configuration config;
    public string Name => "Animation";
    public int Order => 2;
    public bool DisplayTab => true;
    private bool captureAnimation;
    private ICharacter? selectedCharacter;
    private readonly List<(DateTime Time, AttachSet[])> frames = [];
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
        
        if (ImGui.CollapsingHeader("Skeleton"))
        {
            DrawSelectedCharacter();
        }
    }

    private unsafe void Capture()
    {
        if (!captureAnimation) return;
        if (selectedCharacter == null) return;
        
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
        var root = (CharacterBase*)charPtr->GameObject.DrawObject;
        if (root == null)
        {
            logger.LogWarning("CharacterBase is null");
            captureAnimation = false;
            return;
        }

        var attachCollection = new List<AttachSet>();
        var rootSkeleton = new Skeleton.Skeleton(root->Skeleton);
        string rootName;
        if (root->Attach.ExecuteType == 3)
        {
            var owner = root->Attach.OwnerCharacter;
            var rootAttach = new Attach(root->Attach);
            var ownerSkeleton = new Skeleton.Skeleton(owner->Skeleton);
            var attachBoneName = ownerSkeleton.PartialSkeletons[rootAttach.PartialSkeletonIdx].HkSkeleton?.BoneNames[(int)rootAttach.BoneIdx] ?? "Bone";
            rootName = $"{(nint)root:X8}_{attachBoneName}";
            var rootAttachSet = new AttachSet(rootName, rootAttach, rootSkeleton, GetTransform(root), $"{(nint)owner:X8}");
            attachCollection.Add(rootAttachSet);
            attachCollection.Add(new AttachSet($"{(nint)owner:X8}", new Attach(owner->Attach), ownerSkeleton, GetTransform(owner), null));
        } 
        else
        {
            rootName = $"{(nint)root:X8}";
            var rootAttach = new AttachSet(rootName, new Attach(root->Attach), rootSkeleton, GetTransform(root), null);
            attachCollection.Add(rootAttach);
        }

        foreach (var characterAttach in GetAttachData(charPtr, rootSkeleton, rootName))
        {
            // skip ie. mount may be the owner of the character already so we don't want to duplicate
            if (attachCollection.Any(a => a.Id == characterAttach.Id))
            {
                continue;
            }
            attachCollection.Add(characterAttach);
        }
        
        frames.Add((DateTime.UtcNow, attachCollection.ToArray()));
    }
    
    public static unsafe AffineTransform GetTransform(CharacterBase* character)
    {
        var position = character->Position;
        var rotation = character->Rotation;
        var scale = character->Scale;
        return new AffineTransform(scale, rotation, position);
    }
    
    private static unsafe AttachSet[] GetAttachData(Character* charPtr, Skeleton.Skeleton ownerSkeleton, string ownerId)
    {
        var attachments = new List<AttachSet>();
        var ornament = charPtr->OrnamentData.OrnamentObject;
        var companion = charPtr->CompanionData.CompanionObject;
        var mount = charPtr->Mount.MountObject;
        var weaponData = charPtr->DrawData.WeaponData;

        if (ornament != null && ornament->DrawObject != null && ornament->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var ornamentBase = (CharacterBase*)ornament->DrawObject;
            var ornamentAttach = new Attach(ornamentBase->Attach);
            var attachBoneName = ownerSkeleton.PartialSkeletons[ornamentAttach.PartialSkeletonIdx].HkSkeleton?.BoneNames[(int)ornamentAttach.BoneIdx] ?? "Bone";
            attachments.Add(new ($"{(nint)ornamentBase:X8}_{attachBoneName}", ornamentAttach, 
                                 new Skeleton.Skeleton(ornamentBase->Skeleton), GetTransform(ornamentBase), ownerId));
        }

        if (companion != null && companion->DrawObject != null && companion->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var companionBase = (CharacterBase*)companion->DrawObject;
            var companionAttach = new Attach(companionBase->Attach);
            var attachBoneName = ownerSkeleton.PartialSkeletons[companionAttach.PartialSkeletonIdx].HkSkeleton?.BoneNames[(int)companionAttach.BoneIdx] ?? "Bone";
            attachments.Add(new ($"{(nint)companionBase:X8}_{attachBoneName}", companionAttach, 
                                 new Skeleton.Skeleton(companionBase->Skeleton), GetTransform(companionBase), ownerId));
        }

        if (mount != null && mount->DrawObject != null && mount->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var mountBase = (CharacterBase*)mount->DrawObject;
            var mountAttach = new Attach(mountBase->Attach);
            var attachBoneName = ownerSkeleton.PartialSkeletons[mountAttach.PartialSkeletonIdx].HkSkeleton?.BoneNames[(int)mountAttach.BoneIdx] ?? "Bone";
            attachments.Add(new ($"{(nint)mountBase:X8}_{attachBoneName}", mountAttach, 
                                 new Skeleton.Skeleton(mountBase->Skeleton), GetTransform(mountBase), ownerId));
        }

        if (weaponData != null)
        {
            for (var i = 0; i < weaponData.Length; ++i)
            {
                var weapon = weaponData[i];
                if (weapon.DrawObject != null && weapon.DrawObject->GetObjectType() == ObjectType.CharacterBase)
                {
                    var weaponBase = (CharacterBase*)weapon.DrawObject;
                    var weaponAttach = new Attach(weaponBase->Attach);
                    var attachBoneName = ownerSkeleton.PartialSkeletons[weaponAttach.PartialSkeletonIdx].HkSkeleton?.BoneNames[(int)weaponAttach.BoneIdx] ?? "Bone";
                    attachments.Add(new ($"{(nint)weaponBase:X8}_{attachBoneName}", weaponAttach, 
                                         new Skeleton.Skeleton(weaponBase->Skeleton), GetTransform(weaponBase), ownerId));
                }
            }
        }
        
        return attachments.ToArray();
    }

    private unsafe void DrawSelectedCharacter()
    {
        if (selectedCharacter == null) return;
        var charPtr = (Character*)selectedCharacter.Address;
        if (charPtr == null) return;
        var cBase = (CharacterBase*)charPtr->GameObject.DrawObject;
        if (cBase == null) return;
        
        UIUtil.DrawCharacterAttaches(charPtr);
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
