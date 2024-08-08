using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using ImGuiNET;
using Meddle.Plugin.Utils;
using Meddle.Utils.Skeletons;

namespace Meddle.Plugin.UI;

public class DebugTab : ITab
{
    private readonly Configuration config;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;

    public DebugTab(Configuration config, IClientState clientState, IObjectTable objectTable)
    {
        this.config = config;
        this.clientState = clientState;
        this.objectTable = objectTable;
    }
    
    public void Dispose()
    {
        // TODO release managed resources here
    }

    public string Name  => "Debug";
    public int Order => int.MaxValue;
    public bool DisplayTab => config.ShowDebug;
    public void Draw()
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
        
        DrawDebugMenu();
    }

    private unsafe void DrawDebugMenu()
    {
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
        
        ImGui.Text($"Character Name: {character->NameString}");

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
        var skeleton = cBase->Skeleton;
        if (skeleton == null)
        {
            ImGui.Text("Skeleton is null");
            return;
        }
        
        // imgui select partial skeleton by index
        ImGui.Text($"Partial Skeleton Count: {skeleton->PartialSkeletonCount}");
        if (ImGui.InputInt("##PartialSkeletonIndex", ref selectedPartialSkeletonIndex))
        {
            if (selectedPartialSkeletonIndex < 0)
            {
                selectedPartialSkeletonIndex = 0;
            }
            else if (selectedPartialSkeletonIndex >= skeleton->PartialSkeletonCount)
            {
                selectedPartialSkeletonIndex = skeleton->PartialSkeletonCount - 1;
            }
        }
        
        var partialSkeleton = skeleton->PartialSkeletons[selectedPartialSkeletonIndex];
        
        ImGui.Text($"Partial Skeleton Bone Count: {partialSkeleton.BoneCount}");
        if (ImGui.InputInt("##BoneIndex", ref selectedBoneIndex))
        {
            if (selectedBoneIndex < 0)
            {
                selectedBoneIndex = 0;
            }
            else if (selectedBoneIndex >= partialSkeleton.BoneCount)
            {
                selectedBoneIndex = (int)partialSkeleton.BoneCount - 1;
            }
        }

        var pose = partialSkeleton.GetHavokPose(0);
        if (pose == null)
        {
            ImGui.Text("Pose is null");
            return;
        }

        var localPoseValue = pose->LocalPose[selectedBoneIndex];
        var transform = new Transform(localPoseValue);
        ImGui.Text($"Transform: {transform}");
        var poseBone = PoseUtil.AccessBoneLocalSpace(pose, selectedBoneIndex);
        if (poseBone == null)
        {
            ImGui.Text("Pose Bone is null");
            return;
        }
        var boneTransform = new Transform(*poseBone);
        ImGui.Text($"Bone Transform: {boneTransform}");
    }
    
    private int selectedPartialSkeletonIndex;
    private int selectedBoneIndex;

    private ICharacter? selectedCharacter;
    
}
