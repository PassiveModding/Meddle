using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services;
using Meddle.Plugin.Services.UI;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Logging;
using SharpGLTF.Transforms;

namespace Meddle.Plugin.UI;

public class AnimationTab : ITab
{
    private readonly AnimationExportService animationExportService;
    private readonly CommonUi commonUi;
    private readonly Configuration config;
    private readonly List<(DateTime Time, AttachSet[])> frames = [];
    private readonly IFramework framework;
    private readonly ILogger<AnimationTab> logger;
    private bool captureAnimation;
    private bool includePositionalData;
    private ICharacter? selectedCharacter;
    public MenuType MenuType => MenuType.Default;
    private readonly FileDialogManager fileDialog = new()
    {
        AddedWindowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking
    };
    
    public AnimationTab(
        IFramework framework, ILogger<AnimationTab> logger,
        AnimationExportService animationExportService,
        CommonUi commonUi,
        Configuration config)
    {
        this.framework = framework;
        this.logger = logger;
        this.animationExportService = animationExportService;
        this.commonUi = commonUi;
        this.config = config;
        this.framework.Update += OnFrameworkUpdate;
    }

    public string Name => "Animation";
    public int Order => (int) WindowOrder.Animation;

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
            var folderName = $"Animation-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
            fileDialog.SaveFolderDialog("Save Animation", folderName, (result, path) =>
            {
                if (!result) return;
                animationExportService.ExportAnimation(frames, includePositionalData, path);
            }, config.ExportDirectory);   
            
            
        }

        ImGui.SameLine();
        ImGui.Checkbox("Include Positional Data", ref includePositionalData);
        ImGui.Separator();

        DrawSelectedCharacter();
        
        fileDialog.Draw();
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
        string rootName = $"{(nint)root:X8}";
        var attach = StructExtensions.GetAttach(root);
        if (attach.ExecuteType == 3)
        {
            var owner = attach.OwnerCharacter;
            var rootAttach = StructExtensions.GetParsedAttach(root);
            attachCollection.Add(new AttachSet(rootName, rootAttach, StructExtensions.GetParsedSkeleton(root), GetTransform(root), $"{(nint)owner:X8}"));
            attachCollection.Add(new AttachSet($"{(nint)owner:X8}", StructExtensions.GetParsedAttach(owner), StructExtensions.GetParsedSkeleton(owner), GetTransform(owner), null));
        }
        else
        {
            rootName = $"{(nint)root:X8}";
            attachCollection.Add(new AttachSet(rootName, StructExtensions.GetParsedAttach(root), StructExtensions.GetParsedSkeleton(root), GetTransform(root), null));
        }

        foreach (var characterAttach in GetAttachData(charPtr, rootName))
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

    private static unsafe AttachSet[] GetAttachData(Character* charPtr, string ownerId)
    {
        var attachments = new List<AttachSet>();
        var ornament = charPtr->OrnamentData.OrnamentObject;
        // companion disabled as its not actually attached
        Companion* companion = null;//charPtr->CompanionData.CompanionObject;
        var mount = charPtr->Mount.MountObject;
        var weaponData = charPtr->DrawData.WeaponData;

        if (ornament != null && ornament->DrawObject != null &&
            ornament->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var ornamentBase = (CharacterBase*)ornament->DrawObject;
            var ornamentAttach = StructExtensions.GetParsedAttach(ornamentBase);
            attachments.Add(new AttachSet($"{(nint)ornamentBase:X8}", ornamentAttach, StructExtensions.GetParsedSkeleton(ornamentBase),GetTransform(ornamentBase), ownerId));
        }

        if (companion != null && companion->DrawObject != null &&
            companion->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var companionBase = (CharacterBase*)companion->DrawObject;
            var companionAttach = StructExtensions.GetParsedAttach(companionBase);
            attachments.Add(new AttachSet($"{(nint)companionBase:X8}", companionAttach, StructExtensions.GetParsedSkeleton(companionBase), GetTransform(companionBase), ownerId));
        }

        if (mount != null && mount->DrawObject != null &&
            mount->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var mountBase = (CharacterBase*)mount->DrawObject;
            var mountAttach = StructExtensions.GetParsedAttach(mountBase);
            attachments.Add(new AttachSet($"{(nint)mountBase:X8}", mountAttach, StructExtensions.GetParsedSkeleton(mountBase), GetTransform(mountBase), ownerId));
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
                    attachments.Add(new AttachSet($"{(nint)weaponBase:X8}", weaponAttach, StructExtensions.GetParsedSkeleton(weaponBase), GetTransform(weaponBase), ownerId));
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
