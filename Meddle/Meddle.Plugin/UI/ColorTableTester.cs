using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI;

public class ColorTableTester : ITab
{
    private readonly ILogger<ColorTableTester> log;
    private readonly Configuration config;
    private readonly CommonUi commonUi;
    private readonly TextureCache textureCache;
    private readonly ITextureProvider textureProvider;
    private ICharacter? selectedCharacter;
    private OnRenderMaterialOutput? output;
    public string Name => "ColorTable Tester";
    public int Order => 100;
    public MenuType MenuType => MenuType.Debug;
    
    public ColorTableTester(
        ILogger<ColorTableTester> log,
        Configuration config,
        CommonUi commonUi, TextureCache textureCache, ITextureProvider textureProvider)
    {
        this.log = log;
        this.config = config;
        this.commonUi = commonUi;
        this.textureCache = textureCache;
        this.textureProvider = textureProvider;
    }
    
    public unsafe void Draw()
    {
        commonUi.DrawCharacterSelect(ref selectedCharacter, CharacterValidationFlags.IsVisible);
        if (output != null)
        {
            var serialized = System.Text.Json.JsonSerializer.Serialize(output, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            ImGui.TextWrapped(serialized);
            if (output.DecalTexture != null)
            {
                var wrap = textureCache.GetOrAdd($"{output.DecalTexture.GetHashCode()}", () =>
                {
                    var textureData = output.DecalTexture.Bitmap.GetPixelSpan();
                    var wrap = textureProvider.CreateFromRaw(
                        RawImageSpecification.Rgba32(output.DecalTexture.Width, output.DecalTexture.Height), textureData,
                        $"Meddle_Decal_{output.DecalTexture.GetHashCode()}");
                    return wrap;
                });
                var availableWidth = ImGui.GetContentRegionAvail().X;
                float displayWidth = output.DecalTexture.Width;
                float displayHeight = output.DecalTexture.Height;
                if (displayWidth > availableWidth)
                {
                    var ratio = availableWidth / displayWidth;
                    displayWidth *= ratio;
                    displayHeight *= ratio;
                }
                ImGui.Image(wrap.Handle, new Vector2(displayWidth, displayHeight));
            }
            
            if (ImGui.Button("Clear Output"))
            {
                output = null;
            }
        }
        
        if (selectedCharacter == null)
        {
            ImGui.Text("No character selected");
            return;
        }
        
        var character = (Character*)selectedCharacter.Address;
        var drawObject = character->GameObject.DrawObject;
        if (drawObject == null)
        {
            ImGui.Text("Draw object is null");
            return;
        }
            
        if (drawObject->GetObjectType() != ObjectType.CharacterBase)
        {
            ImGui.Text("Draw object is not a character base");
            return;
        }
            
        var cBase = (CharacterBase*)drawObject;
        var modelType = cBase->GetModelType();
        if (modelType != CharacterBase.ModelType.Human)
        {
            ImGui.Text("Model is not human");
            return;
        }
        
        
        // cPtr.Value->ColorTableTexturesSpan[(slotIdx * CSCharacterBase.MaterialsPerSlot) + materialIdx];
        var human = (Human*)cBase;
        for (var modelIdx = 0; modelIdx < human->ModelsSpan.Length; modelIdx++)
        {
            var model = human->ModelsSpan[modelIdx];
            if (model == null || model.Value == null)
            {
                continue;
            }

            for (var materialIdx = 0; materialIdx < model.Value->MaterialsSpan.Length; materialIdx++)
            {
                var material = model.Value->MaterialsSpan[materialIdx];
                if (material == null || material.Value == null)
                {
                    continue;
                }
                
                
            }
        }
        for (var colorTableIdx = 0; colorTableIdx < human->ColorTableTexturesSpan.Length; colorTableIdx++)
        {
            var colorTableTexture = human->ColorTableTexturesSpan[colorTableIdx];
            if (colorTableTexture == null || colorTableTexture.Value == null)
            {
                // ImGui.Text($"Color table texture {i} is null");
                continue;
            }

            var slotIdx = colorTableIdx / CharacterBase.MaterialsPerSlot;
            var materialIdx = colorTableIdx % CharacterBase.MaterialsPerSlot;
            
            if (ImGui.CollapsingHeader($"Color Table Texture {colorTableIdx} - {colorTableTexture.Value->ActualWidth}x{colorTableTexture.Value->ActualHeight}"))
            {
                var colorTable = ParseMaterialUtil.ParseColorTableTexture(colorTableTexture);
                UiUtil.DrawColorTable(colorTable);
            }
        }
    }
    
    
    public void Dispose()
    {
        // TODO release managed resources here
    }
}
