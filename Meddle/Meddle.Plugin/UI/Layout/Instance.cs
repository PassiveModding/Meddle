using System.Numerics;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using ImGuiNET;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;

namespace Meddle.Plugin.UI.Layout;

public partial class LayoutWindow
{
    private void DrawInstanceTable(IEnumerable<ParsedInstance> instances, Action<Stack<ParsedInstance>, ParsedInstance>? additionalOptions = null)
    {
        using var table =
            ImRaii.Table("##layoutTable", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | 
                                             ImGuiTableFlags.Reorderable);
        ImGui.TableSetupColumn("Data", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Options");
        foreach (var instance in instances.Take(maxItemCount))
        {
            DrawInstance(instance, [], additionalOptions);
        }
    }

    private void DrawInstance(ParsedInstance instance, Stack<ParsedInstance> stack, Action<Stack<ParsedInstance>, ParsedInstance>? additionalOptions = null)
    {
        if (stack.Count > 10)
        {
            ImGui.Text("Max depth reached");
            return;
        }

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        using var stackScope = stack.PushScoped(instance);
        using var id = ImRaii.PushId(instance.Id);
        var infoHeader = instance switch
        {
            ParsedHousingInstance housingObject => $"{housingObject.Type} - {housingObject.Name}",
            ParsedBgPartsInstance bgObject => $"{bgObject.Type} - {bgObject.Path.GamePath}",
            ParsedUnsupportedInstance unsupported => $"{unsupported.Type} - {unsupported.InstanceType}",
            ParsedCharacterInstance character => $"{character.Type} - {character.Kind}",
            _ => $"{instance.Type}"
        };
        
        if (instance is ParsedCharacterInstance ci)
        {
            if (!string.IsNullOrWhiteSpace(config.PlayerNameOverride) && ci.Kind == ObjectKind.Pc)
            {
                infoHeader += $" - {config.PlayerNameOverride}";
            }
            else
            {
                infoHeader += $" - {ci.Name}";
            }
        }
        
        if (instance is ParsedSharedInstance {Children.Count: > 0} shared)
        {
            var childTypeGroups = shared.Children.GroupBy(x => x.Type);
            // type[count], type[count]
            var childTypes = string.Join(", ", childTypeGroups.Select(x => $"{x.Key}[{x.Count()}]"));
            infoHeader += $" ({childTypes})";
        }
        
        var distance = Vector3.Distance(instance.Transform.Translation, sigUtil.GetLocalPosition());
        using var displayInner = ImRaii.TreeNode($"[{distance:F1}y] {infoHeader}###{instance.Id}");
        if (displayInner.Success)
        {
            UiUtil.Text($"Id: {instance.Id}", $"{instance.Id:X8}");
            ImGui.Text($"Type: {instance.Type}");
            ImGui.Text($"Position: {instance.Transform.Translation}");
            ImGui.Text($"Rotation: {instance.Transform.Rotation}");
            ImGui.Text($"Scale: {instance.Transform.Scale}");
            if (instance is IPathInstance pathedInstance)
            {
                UiUtil.Text($"Full Path: {pathedInstance.Path.FullPath}", pathedInstance.Path.FullPath);
                UiUtil.Text($"Game Path: {pathedInstance.Path.GamePath}", pathedInstance.Path.GamePath);
            }
            
            if (instance is ParsedLightInstance light)
            {
                ImGui.ColorButton("Color", light.Color);
            }
            
            if (instance is ParsedHousingInstance ho)
            {
                ImGui.Text($"Kind: {ho.Kind}");
                ImGui.Text($"Object Name: {ho.Name}");
                ImGui.Text($"Item Name: {ho.Item?.Name}");
                Vector4? color = ho.Stain == null ? null : ImGui.ColorConvertU32ToFloat4(ho.Stain.Color);
                if (color != null)
                {
                    ImGui.ColorButton("Stain", color.Value);
                }
                else
                {
                    ImGui.Text("No Stain");
                }
            }

            if (instance is ParsedCharacterInstance character)
            {
                DrawCharacter(character);
            }
        }
        
        ImGui.TableSetColumnIndex(1);
        additionalOptions?.Invoke(stack, instance);
        
        if (displayInner.Success && instance is ParsedSharedInstance childShared)
        {
            foreach (var obj in childShared.Children)
            {
                DrawInstance(obj, stack, additionalOptions);
            }
        }
    }

    private void DrawCharacter(ParsedCharacterInstance character)
    {
        ImGui.Text($"Kind: {character.Kind}");
        ImGui.Text($"Name: {character.Name}");
        if (character.CharacterInfo == null) return;
        ImGui.Text("Models");
        foreach (var modelInfo in character.CharacterInfo.Models)
        {
            using var treeNode = ImRaii.TreeNode($"Model: {modelInfo.Path.GamePath}");
            if (treeNode.Success)
            {
                UiUtil.Text($"Model Path: {modelInfo.Path.FullPath}", modelInfo.Path.FullPath);
                UiUtil.Text($"Game Path: {modelInfo.Path.GamePath}", modelInfo.Path.GamePath);
                if (modelInfo.Deformer != null)
                {
                    UiUtil.Text($"Deformer Path: {modelInfo.Deformer.Value.PbdPath}", modelInfo.Deformer.Value.PbdPath);
                    ImGui.Text($"Deformer Id: {modelInfo.Deformer.Value.DeformerId}");
                    ImGui.Text($"Race Sex Id: {modelInfo.Deformer.Value.RaceSexId}");
                }

                foreach (var materialInfo in modelInfo.Materials)
                {
                    using var materialNode = ImRaii.TreeNode($"Material: {materialInfo.Path.GamePath}");
                    if (materialNode.Success)
                    {
                        UiUtil.Text($"Material Path: {materialInfo.Path.FullPath}", materialInfo.Path.FullPath);
                        UiUtil.Text($"Game Path: {materialInfo.Path.GamePath}", materialInfo.Path.GamePath);
                        UiUtil.Text($"Shader Path: {materialInfo.Shpk}", materialInfo.Shpk);
                        ImGui.Text($"Texture Count: {materialInfo.Textures.Count}");
                        foreach (var textureInfo in materialInfo.Textures)
                        {
                            using var textureNode = ImRaii.TreeNode($"Texture: {textureInfo.Path.GamePath}");
                            if (textureNode.Success)
                            {
                                UiUtil.Text($"Texture Path: {textureInfo.Path.FullPath}", textureInfo.Path.FullPath);
                                UiUtil.Text($"Game Path: {textureInfo.Path.GamePath}", textureInfo.Path.GamePath);
                                DrawTexture(textureInfo.Path.FullPath, textureInfo.Resource);
                            }
                        }
                    }
                }
            }
        }
    }

    private void DrawTexture(string path, TextureResource resource)
    {
        ImGui.Text($"Width: {resource.Width}");
        ImGui.Text($"Height: {resource.Height}");
        ImGui.Text($"Format: {resource.Format}");
        ImGui.Text($"Mipmap Count: {resource.MipLevels}");
        ImGui.Text($"Array Count: {resource.ArraySize}");
        
        
        var wrap = textureCache.GetOrAdd(path, () =>
        {
            var texture = resource.ToBitmap();
            var wrap = textureProvider.CreateFromRaw(
                RawImageSpecification.Rgba32(texture.Width, texture.Height), texture.GetPixelSpan());
            return wrap;
        });
        
        var availableWidth = ImGui.GetContentRegionAvail().X;
        float displayWidth = wrap.Width;
        float displayHeight = wrap.Height;
        if (displayWidth > availableWidth)
        {
            var ratio = availableWidth / displayWidth;
            displayWidth *= ratio;
            displayHeight *= ratio;
        }
        
        ImGui.Image(wrap.ImGuiHandle, new Vector2(displayWidth, displayHeight));
    }
}
