using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using Dalamud.Bindings.ImGui;
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
    private int intervalMs = 100;
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

    private List<ICharacter> selectedCharacters = [];
    
    public void Draw()
    {
        // Warning text:
        ImGui.TextWrapped(
            "NOTE: Animation exports are experimental, held weapons, mounts and other attached objects may not work as expected.");

        commonUi.DrawMultiCharacterSelect(ref selectedCharacters);
        
        if (ImGui.InputInt("Interval (ms)", ref intervalMs, 10, 100))
        {
            if (intervalMs < 50) intervalMs = 50;
            if (intervalMs > 1000) intervalMs = 1000;
        }

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

        foreach (var selectedCharacter in selectedCharacters)
        {
            if (ImGui.CollapsingHeader(UiUtil.GetCharacterName(selectedCharacter.Name.TextValue, config, (ObjectKind)selectedCharacter.ObjectKind), ImGuiTreeNodeFlags.DefaultOpen))
            {
                using var id = ImRaii.PushId(selectedCharacter.Address);
                DrawSelectedCharacter(selectedCharacter);
            }
        }
        
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

        if (frames.Count > 0 && DateTime.UtcNow - frames[^1].Time < TimeSpan.FromMilliseconds(intervalMs))
        {
            return;
        }

        var characters = commonUi.GetCharacters()
                                 .Where(x => selectedCharacters.Any(s => s.Address == x.Address)).ToArray();
        var attachCollection = new List<AttachSet>();
        foreach (var character in characters)
        {
            var charPtr = (Character*)character.Address;
            if (charPtr == null)
            {
                logger.LogWarning("Character is null");
                return;
            }

            var root = (CharacterBase*)charPtr->GameObject.DrawObject;
            if (root == null)
            {
                logger.LogWarning("CharacterBase is null");
                return;
            }

            string rootName = $"{(nint)root:X8}";
            var attach = root->Attach;
            string actorName = charPtr->NameString;
            if (attach.ExecuteType == 3)
            {
                var owner = attach.OwnerCharacter;
                var rootAttach = StructExtensions.GetParsedAttach(root);
                attachCollection.Add(new AttachSet(rootName, $"Actor_{actorName}", rootAttach, StructExtensions.GetParsedSkeleton(root), GetTransform(root), $"{(nint)owner:X8}"));
                attachCollection.Add(new AttachSet($"{(nint)owner:X8}", $"Owner_{actorName}", StructExtensions.GetParsedAttach(owner), StructExtensions.GetParsedSkeleton(owner), GetTransform(owner), null));
            }
            else
            {
                rootName = $"{(nint)root:X8}";
                attachCollection.Add(new AttachSet(rootName, $"Actor_{actorName}", StructExtensions.GetParsedAttach(root), StructExtensions.GetParsedSkeleton(root), GetTransform(root), null));
            }

            foreach (var characterAttach in GetAttachData(charPtr, rootName, actorName))
            {
                // skip ie. mount may be the owner of the character already so we don't want to duplicate
                if (attachCollection.Any(a => a.Id == characterAttach.Id))
                {
                    continue;
                }

                attachCollection.Add(characterAttach);
            }
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

    private static unsafe AttachSet[] GetAttachData(Character* charPtr, string ownerId, string actorName)
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
            attachments.Add(new AttachSet($"{(nint)ornamentBase:X8}", $"Ornament_{actorName}", ornamentAttach, StructExtensions.GetParsedSkeleton(ornamentBase),GetTransform(ornamentBase), ownerId));
        }

        if (companion != null && companion->DrawObject != null &&
            companion->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var companionBase = (CharacterBase*)companion->DrawObject;
            var companionAttach = StructExtensions.GetParsedAttach(companionBase);
            attachments.Add(new AttachSet($"{(nint)companionBase:X8}", $"Companion_{actorName}", companionAttach, StructExtensions.GetParsedSkeleton(companionBase), GetTransform(companionBase), ownerId));
        }

        if (mount != null && mount->DrawObject != null &&
            mount->DrawObject->GetObjectType() == ObjectType.CharacterBase)
        {
            var mountBase = (CharacterBase*)mount->DrawObject;
            var mountAttach = StructExtensions.GetParsedAttach(mountBase);
            attachments.Add(new AttachSet($"{(nint)mountBase:X8}", $"Mount_{actorName}", mountAttach, StructExtensions.GetParsedSkeleton(mountBase), GetTransform(mountBase), ownerId));
        }


        for (var i = 0; i < weaponData.Length; ++i)
        {
            var weapon = weaponData[i];
            if (weapon.DrawObject != null && weapon.DrawObject->GetObjectType() == ObjectType.CharacterBase)
            {
                var weaponBase = (CharacterBase*)weapon.DrawObject;
                var weaponAttach = StructExtensions.GetParsedAttach(weaponBase);                    
                attachments.Add(new AttachSet($"{(nint)weaponBase:X8}", $"Weapon{i}_{actorName}", weaponAttach, StructExtensions.GetParsedSkeleton(weaponBase), GetTransform(weaponBase), ownerId));
            }
        }

        return attachments.ToArray();
    }

    private unsafe void DrawSelectedCharacter(ICharacter? selectedCharacter = null)
    {
        if (selectedCharacter == null) return;
        var charPtr = (Character*)selectedCharacter.Address;
        if (charPtr == null) return;

        UiUtil.DrawCharacterAttaches(charPtr);
    }
}
