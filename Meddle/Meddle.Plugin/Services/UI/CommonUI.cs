using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Dalamud.Bindings.ImGui;
using Meddle.Plugin.Utils;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace Meddle.Plugin.Services.UI;

public class CommonUi : IDisposable, IService
{
    private readonly IObjectTable objectTable;
    private readonly IClientState clientState;
    private readonly Configuration config;
    private bool selectTarget;

    public CommonUi(IClientState clientState, IObjectTable objectTable, Configuration config)
    {
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.config = config;
    }
    
    public unsafe ICharacter[] GetCharacters()
    {
        if (clientState.LocalPlayer != null)
        {
            return objectTable.OfType<ICharacter>()
                              .Where(obj => obj.IsValid() && obj.IsValidCharacterBase())
                              .OrderBy(c => clientState.GetDistanceToLocalPlayer(c).LengthSquared())
                              .ToArray();
        }
        else
        {
            // login/char creator produces "invalid" characters but are still usable I guess
            return objectTable.OfType<ICharacter>()
                              .Where(obj => obj.IsValidHuman())
                              .OrderBy(c => clientState.GetDistanceToLocalPlayer(c).LengthSquared())
                              .ToArray();
        }
    }

    public unsafe void DrawMultiCharacterSelect(ref List<ICharacter> selectedCharacters)
    {
        ICharacter[] objects = GetCharacters();

        ImGui.Text("Select Characters");
        using var combo = ImRaii.Combo("##MultiCharacter", $"{selectedCharacters.Count} Selected");
        if (combo)
        {
            foreach (var character in objects)
            {
                var displayText = GetCharacterDisplayText(character);
                var contains = selectedCharacters.Contains(character);
                using var col = ImRaii.PushColor(ImGuiCol.Text, contains ? ImGuiColors.DalamudYellow : ImGuiColors.DalamudWhite);
                if (ImGui.Selectable(displayText, contains, ImGuiSelectableFlags.DontClosePopups))
                {
                    if (contains)
                    {
                        selectedCharacters.Remove(character);
                    }
                    else
                    {
                        selectedCharacters.Add(character);
                    }
                }
            }
        }
        
        // remove entries which cannot be found in the object table
        foreach (var character in selectedCharacters.ToArray())
        {
            if (!objectTable.Contains(character) || !character.IsValid() || !character.IsValidCharacterBase())
            {
                selectedCharacters.Remove(character);
            }
        }
    }

    public unsafe void DrawCharacterSelect(ref ICharacter? selectedCharacter)
    {
        ICharacter[] objects = GetCharacters();

        selectedCharacter ??= objects.FirstOrDefault() ?? clientState.LocalPlayer;

        ImGui.Text("Select Character");
        if (selectedCharacter?.IsValid() == false && clientState.LocalPlayer != null)
        {
            selectedCharacter = null;
        }

        var preview = selectedCharacter != null ? GetCharacterDisplayText(selectedCharacter) : "None";
        using var combo = ImRaii.Combo("##Character", preview);
        if (combo)
        {
            foreach (var character in objects)
            {
                if (ImGui.Selectable(GetCharacterDisplayText(character)))
                {
                    selectedCharacter = character;
                }
            }
        }
        
        ImGui.Checkbox("Select Target", ref selectTarget);
        
        if (selectTarget)
        {
            if (clientState.LocalPlayer is {TargetObject: not null})
            {
                var target = clientState.LocalPlayer.TargetObject;
                if (target is ICharacter targetCharacter && targetCharacter.IsValid() && targetCharacter.IsValidCharacterBase())
                {
                    selectedCharacter = targetCharacter;
                }
                else
                {
                    ImGui.Text("Target is not a valid character");
                }
            }
        }

        if (selectedCharacter != null)
        {
            var charPtr = (Character*)selectedCharacter.Address;
            var drawObj = charPtr->DrawObject;
            if (drawObj != null && !drawObj->IsVisible)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1, 1, 0, 1)))
                {
                    ImGui.Text("Character is not visible");
                }

                if (clientState.IsGPosing)
                {
                    ImGui.SameLine();        
                    using (ImRaii.PushFont(UiBuilder.IconFont))
                    {
                        ImGui.Text(FontAwesomeIcon.QuestionCircle.ToIconString());
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text(
                            "When in gpose, copies of targetable actors are created, " +
                            "if you're not expecting this, try selecting the character from the list again");
                        ImGui.EndTooltip();
                    }
                }
            }
        }
    }
    
    public unsafe string GetCharacterDisplayText(IGameObject obj)
    {
        var drawObject = ((GameObject*)obj.Address)->DrawObject;
        if (drawObject == null)
            return "Invalid Character";

        if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase)
            return "Invalid Character";

        var modelType = ((CharacterBase*)drawObject)->GetModelType();

        var name = obj.Name.TextValue;
        if (obj.ObjectKind == ObjectKind.Player && !string.IsNullOrWhiteSpace(config.PlayerNameOverride))
            name = config.PlayerNameOverride;
        return
            $"[{obj.Address:X8}:{obj.GameObjectId:X}][{obj.ObjectKind}][{modelType}] - " +
            $"{(string.IsNullOrWhiteSpace(name) ? "Unnamed" : name)} - " +
            $"{clientState.GetDistanceToLocalPlayer(obj).Length():0.00}y##{obj.GameObjectId}";
    }

    public void Dispose()
    {
    }
}
