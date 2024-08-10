using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Services;
using Meddle.Plugin.Services.UI;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Logging;
using SharpGLTF.Transforms;

namespace Meddle.Plugin.UI;

public class AnimationTab : ITab
{
    private readonly ExportService exportService;
    private readonly CommonUi commonUi;
    private readonly List<(DateTime Time, AttachSet[])> frames = [];
    private readonly IFramework framework;
    private readonly ILogger<AnimationTab> logger;
    private bool captureAnimation;
    private bool includePositionalData;
    private ICharacter? selectedCharacter;

    public AnimationTab(
        IFramework framework, ILogger<AnimationTab> logger,
        ExportService exportService,
        CommonUi commonUi)
    {
        this.framework = framework;
        this.logger = logger;
        this.exportService = exportService;
        this.commonUi = commonUi;
        this.framework.Update += OnFrameworkUpdate;
    }

    public string Name => "Animation";
    public int Order => 2;
    public bool DisplayTab => true;

    public void Draw()
    {
        // Warning text:
        ImGui.TextWrapped(
            "NOTE: Animation exports are experimental, held weapons, mounts and other attached objects may not work as expected.");

        commonUi.DrawCharacterSelect(ref selectedCharacter);
        if (selectedCharacter == null) return;

        switch (captureAnimation)
        {
            case true when ImGui.Button("Stop Capture"):
                captureAnimation = false;
                logger.LogInformation("Stopped capturing animation");
                break;
            case false when ImGui.Button("Start Capture"):
                captureAnimation = true;
                logger.LogInformation("Capturing animation");
                break;
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Clear"))
        {
            frames.Clear();
        }

        ImGui.SameLine();
        var frameCount = frames.Count;
        ImGui.Text($"Frames: {frameCount}");
        
        
        if (ImGui.Button("Export"))
        {
            exportService.ExportAnimation(frames, includePositionalData);
        }

        ImGui.SameLine();
        ImGui.Checkbox("Include Positional Data", ref includePositionalData);
        ImGui.Separator();

        DrawSelectedCharacter();
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework1)
    {
        Capture();
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
        var rootSkeleton = StructExtensions.GetParsedSkeleton(root);
        string rootName;
        var attach = StructExtensions.GetAttach(root);
        if (attach.ExecuteType == 3)
        {
            var owner = attach.OwnerCharacter;
            var rootAttach = StructExtensions.GetParsedAttach(root);
            var ownerSkeleton = StructExtensions.GetParsedSkeleton(owner);
            var attachBoneName = ownerSkeleton.PartialSkeletons[rootAttach.PartialSkeletonIdx].HkSkeleton
                                              ?.BoneNames[(int)rootAttach.BoneIdx] ?? "Bone";
            rootName = $"{(nint)root:X8}_{attachBoneName}";
            var rootAttachSet =
                new AttachSet(rootName, rootAttach, rootSkeleton, GetTransform(root), $"{(nint)owner:X8}");
            attachCollection.Add(rootAttachSet);
            attachCollection.Add(new AttachSet($"{(nint)owner:X8}", StructExtensions.GetParsedAttach(owner),
                                               ownerSkeleton, GetTransform(owner), null));
        }
        else
        {
            rootName = $"{(nint)root:X8}";
            var rootAttach = new AttachSet(rootName, StructExtensions.GetParsedAttach(root), rootSkeleton,
                                           GetTransform(root), null);
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

    public static unsafe AffineTransform GetTransform(Pointer<CharacterBase> characterPointer)
    {
        if (characterPointer == null || characterPointer.Value == null)
            throw new ArgumentNullException(nameof(characterPointer));
        var character = characterPointer.Value;
        var position = character->Position;
        var rotation = character->Rotation;
        var scale = character->Scale;
        return new AffineTransform(scale, rotation, position);
    }

    private static unsafe AttachSet[] GetAttachData(Character* charPtr, ParsedSkeleton ownerSkeleton, string ownerId)
    {
        var attachments = new List<AttachSet>();
        var ornament = charPtr->OrnamentData.OrnamentObject;
        var companion = charPtr->CompanionData.CompanionObject;
        var mount = charPtr->Mount.MountObject;
        var weaponData = charPtr->DrawData.WeaponData;

        if (ornament != null && ornament->DrawObject != null &&
            ornament->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var ornamentBase = (CharacterBase*)ornament->DrawObject;
            var ornamentAttach = StructExtensions.GetParsedAttach(ornamentBase);
            var attachBoneName = ownerSkeleton.PartialSkeletons[ornamentAttach.PartialSkeletonIdx].HkSkeleton
                                              ?.BoneNames[(int)ornamentAttach.BoneIdx] ?? "Bone";
            attachments.Add(new AttachSet($"{(nint)ornamentBase:X8}_{attachBoneName}", ornamentAttach,
                                          StructExtensions.GetParsedSkeleton(ornamentBase), GetTransform(ornamentBase),
                                          ownerId));
        }

        if (companion != null && companion->DrawObject != null &&
            companion->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var companionBase = (CharacterBase*)companion->DrawObject;
            var companionAttach = StructExtensions.GetParsedAttach(companionBase);
            var attachBoneName = ownerSkeleton.PartialSkeletons[companionAttach.PartialSkeletonIdx].HkSkeleton
                                              ?.BoneNames[(int)companionAttach.BoneIdx] ?? "Bone";
            attachments.Add(new AttachSet($"{(nint)companionBase:X8}_{attachBoneName}", companionAttach,
                                          StructExtensions.GetParsedSkeleton(companionBase),
                                          GetTransform(companionBase), ownerId));
        }

        if (mount != null && mount->DrawObject != null &&
            mount->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var mountBase = (CharacterBase*)mount->DrawObject;
            var mountAttach = StructExtensions.GetParsedAttach(mountBase);
            var attachBoneName = ownerSkeleton.PartialSkeletons[mountAttach.PartialSkeletonIdx].HkSkeleton
                                              ?.BoneNames[(int)mountAttach.BoneIdx] ?? "Bone";
            attachments.Add(new AttachSet($"{(nint)mountBase:X8}_{attachBoneName}", mountAttach,
                                          StructExtensions.GetParsedSkeleton(mountBase), GetTransform(mountBase),
                                          ownerId));
        }

        if (weaponData != null)
        {
            for (var i = 0; i < weaponData.Length; ++i)
            {
                var weapon = weaponData[i];
                if (weapon.DrawObject != null && weapon.DrawObject->GetObjectType() == ObjectType.CharacterBase)
                {
                    var weaponBase = (CharacterBase*)weapon.DrawObject;
                    var weaponAttach = StructExtensions.GetParsedAttach(weaponBase);
                    var attachBoneName = ownerSkeleton.PartialSkeletons[weaponAttach.PartialSkeletonIdx].HkSkeleton
                                                      ?.BoneNames[(int)weaponAttach.BoneIdx] ?? "Bone";
                    attachments.Add(new AttachSet($"{(nint)weaponBase:X8}_{attachBoneName}", weaponAttach,
                                                  StructExtensions.GetParsedSkeleton(weaponBase),
                                                  GetTransform(weaponBase), ownerId));
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

        UiUtil.DrawCharacterAttaches(charPtr);
    }
}
