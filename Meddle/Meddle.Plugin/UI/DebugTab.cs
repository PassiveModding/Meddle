using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using ImGuiNET;
using Meddle.Plugin.Utils;

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

    private void DrawDebugMenu()
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
        
    }

    private ICharacter? selectedCharacter;
    
}
