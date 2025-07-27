using System.Numerics;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer;
using ImGuiNET;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Helpers;

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
        
        foreach (var instance in instances
                     .Take(config.LayoutConfig.MaxItemCount))
        {
            DrawInstance(instance, [], additionalOptions);
        }
    }
    
    private unsafe void DrawControlsEvil(ParsedInstance instance)
    {
        // validate instance still exists
        if (currentLayout.All(i => i.Id != instance.Id)) return;
        
        var layoutInstance = (ILayoutInstance*)instance.Id;
        var graphics = layoutInstance->GetGraphics();
        if (graphics == null) return;
        Vector3 translation = graphics->Position;

        using var _ = ImRaii.PushId(instance.Id);
        if (ImGui.DragFloat3("Position", ref translation, 0.1f))
        {
            // WARNING: Don't use this, it will move the collision of the object, instead just set translation on the graphics back
            // bgPartPtr->SetTranslationImpl(&translation);
            graphics->Position = translation;
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
            ParsedBgPartsInstance bgObject => $"{bgObject.Type} - {bgObject.Path.GamePath} - {(bgObject.IsVisible ? "Visible" : "Hidden")}",
            ParsedUnsupportedInstance unsupported => $"{unsupported.Type} - {unsupported.InstanceType}",
            ParsedCharacterInstance character => $"{character.Type} - {character.Kind}",
            _ => $"{instance.Type}"
        };
        
        if (instance is ParsedCharacterInstance ci)
        {
            infoHeader += $" - {ci.Name}";
        }
        
        if (instance is ParsedSharedInstance {Children.Count: > 0} shared)
        {
            var childTypeGroups = shared.Children.GroupBy(x => x.Type);
            // type[count], type[count]
            var childTypes = string.Join(", ", childTypeGroups.Select(x => $"{x.Key}[{x.Count()}]"));
            infoHeader += $" ({childTypes})";
        }
        
        var distance = Vector3.Distance(instance.Transform.Translation, searchOrigin);
        using var displayInner = ImRaii.TreeNode($"[{distance:F1}y] {infoHeader}###{instance.Id}");
        if (displayInner.Success)
        {
            UiUtil.Text($"Id: {instance.Id}", $"{instance.Id:X8}");
            ImGui.Text($"Type: {instance.Type}");
            ImGui.Text($"Position: {instance.Transform.Translation}");
            ImGui.Text($"Rotation: {instance.Transform.Rotation}");
            ImGui.Text($"Scale: {instance.Transform.Scale}");

            if (config.SecretConfig == "Kweh")
            {
                DrawControlsEvil(instance);
            }

            if (instance is IPathInstance pathedInstance)
            {
                UiUtil.Text($"Full Path: {pathedInstance.Path.FullPath}", pathedInstance.Path.FullPath);
                UiUtil.Text($"Game Path: {pathedInstance.Path.GamePath}", pathedInstance.Path.GamePath);
            }
            
            if (instance is ParsedLightInstance light)
            {
                ImGui.ColorButton("Color", new Vector4(light.Light.Color.Rgb, light.Light.Color.Intensity));
            }
        
            if (instance is ParsedEnvLightInstance envLight)
            {
                var lt = envLight.Lighting;
                ImGui.ColorButton("Sunlight", new Vector4(lt.SunLightColor.Rgb, lt.SunLightColor.HdrIntensity));
                ImGui.ColorButton("Moonlight", new Vector4(lt.MoonLightColor.Rgb, lt.MoonLightColor.HdrIntensity));
                ImGui.ColorButton("Ambient", new Vector4(lt.Ambient.Rgb, lt.Ambient.HdrIntensity));
            }

            if (instance is ParsedWorldDecalInstance decal)
            {
                UiUtil.Text($"Diffuse Path: {decal.Diffuse.FullPath}", decal.Diffuse.FullPath);
                UiUtil.Text($"Normal Path: {decal.Normal.FullPath}", decal.Normal.FullPath);
                UiUtil.Text($"Specular Path: {decal.Specular.FullPath}", decal.Specular.FullPath);
            }

            if (instance is ParsedCameraInstance cameraInstance)
            {
                ImGui.Text($"FoV: {cameraInstance.FoV}");
                ImGui.Text($"Aspect Ratio: {cameraInstance.AspectRatio}");
                ImGui.Text($"Actual Rotation: {cameraInstance.Rotation}");
            }
            
            if (instance is ParsedHousingInstance ho)
            {
                ImGui.Text($"Kind: {ho.Kind}");
                ImGui.Text($"Object Name: {ho.Name}");
                //ImGui.Text($"Item Name: {ho.Item?.Name}");
                if (ho.Stain != null)
                {
                    ImGui.Text($"Stain ({ho.Stain.RowId})");
                    ImGui.SameLine();
                    ImGui.ColorButton("Stain", ho.Stain.Color);
                }
                else
                {
                    ImGui.Text("No Stain");
                }
                
                ImGui.Text($"Default Stain {ho.DefaultStain.RowId}");
                ImGui.SameLine();
                ImGui.ColorButton("Default Stain", ho.DefaultStain.Color);
            }
            else if (instance is IStainableInstance stainable)
            {
                if (stainable.Stain != null)
                {
                    ImGui.Text("Stain");
                    ImGui.SameLine();
                    ImGui.ColorButton("Stain", stainable.Stain.Color);
                }
                else
                {
                    ImGui.Text("No Stain");
                }
            }
            
            if (instance is ParsedBgPartsInstance bgPart)
            {
                // DrawCache(bgPart);
                DrawBgObject(bgPart);
            }

            if (instance is ParsedCharacterInstance character)
            {
                DrawCharacter(character);
            }

            if (instance is ParsedTerrainInstance terrain)
            {
                DrawTerrain(terrain);
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

    private unsafe void DrawBgObject(ParsedBgPartsInstance bg)
    {
        BgPartsLayoutInstance* bgPartLayout = (BgPartsLayoutInstance*)bg.Id;
    
        if (bgPartLayout->GraphicsObject != null)
        {
            BgObject* drawObject = bgPartLayout->GraphicsObject;
            UiUtil.Text($"Graphics Object {(nint)drawObject:X8}", $"{(nint)drawObject:X8}");
            if (bg.BgChangeMaterial != null)
            {
                ImGui.Text($"BgChangeMaterialPath: {bg.BgChangeMaterial.Value.MaterialPath}");
            }
            
            using var disabled = ImRaii.Disabled( mdlMaterialWindowManager.HasWindow(drawObject->ModelResourceHandle));
            if (ImGui.Button("Open Material Window"))
            {
                mdlMaterialWindowManager.AddMaterialWindow(drawObject->ModelResourceHandle);
            }
        }
    }

    private void DrawTerrain(ParsedTerrainInstance terrain)
    {
        if (terrain.Data == null) return;
        var file = terrain.Data.TeraFile;
        ImGui.Text($"Plate Count: {file.Header.PlateCount}");
        ImGui.Text($"Plate Size: {file.Header.PlateSize}");
        ImGui.Text($"Clip Distance: {file.Header.ClipDistance}");

        using var treeNode = ImRaii.TreeNode("Plates");
        if (treeNode.Success)
        {
            for (int i = 0; i < file.Header.PlateCount; i++)
            {
                using var plateNode = ImRaii.TreeNode($"Plate {i}");
                if (!plateNode.Success) continue;
                var position = file.GetPlatePosition(i);
                ImGui.Text($"Position: {position}");
                if (!terrain.Data.ResolvedPlates.TryGetValue(i, out var plate))
                {
                    var path = $"{terrain.Path.GamePath}/bgplate/{i:D4}.mdl";
                    plate = resolverService.ParseModelFromPath(path);
                    terrain.Data.ResolvedPlates[i] = plate;
                }
                
                if (plate == null)
                {
                    ImGui.Text("No plate data");
                    continue;
                }
                UiUtil.Text($"Model Path: {plate.Path.FullPath}", plate.Path.FullPath);
                UiUtil.Text($"Game Path: {plate.Path.GamePath}", plate.Path.GamePath);
                
                foreach (var material in plate.Materials)
                {
                    if (material == null) continue;
                    using var materialNode = ImRaii.TreeNode($"Material: {material.Path.GamePath}");
                    if (!materialNode.Success) continue;
                    UiUtil.Text($"Material Path: {material.Path.FullPath}", material.Path.FullPath);
                    UiUtil.Text($"Game Path: {material.Path.GamePath}", material.Path.GamePath);
                    UiUtil.Text($"Shader Path: {material.Shpk}", material.Shpk);
                    ImGui.Text($"Texture Count: {material.Textures.Length}");
                    foreach (var texture in material.Textures)
                    {
                        using var textureNode = ImRaii.TreeNode($"Texture: {texture.Path.GamePath}");
                        if (!textureNode.Success) continue;
                        UiUtil.Text($"Texture Path: {texture.Path.FullPath}", texture.Path.FullPath);
                        UiUtil.Text($"Game Path: {texture.Path.GamePath}", texture.Path.GamePath);
                        DrawTexture(texture.Path.FullPath, texture.Resource);
                    }
                }
            }
        }
    }

    /*private void DrawCache(ParsedInstance instance)
    {
        if (instance is not ParsedBgPartsInstance bgPartInstance) return;
        var mdlPath = bgPartInstance.Path.FullPath;
        
        if (!mdlCache.TryGetValue(mdlPath, out var cachedMdl))
        {
            try
            {
                var mdlFile = dataManager.GetFileOrReadFromDisk(mdlPath);
                if (mdlFile == null)
                {
                    mdlCache[mdlPath] = null;
                }
                else
                {
                    mdlCache[mdlPath] = new MdlFile(mdlFile);
                }
            }
            catch (Exception e)
            {
                log.LogError(e, "Failed to load mdl file");
                mdlCache[mdlPath] = null;
            }
                
            cachedMdl = mdlCache[mdlPath];
        }
            
        if (cachedMdl == null)
        {
            ImGui.Text("No cached mdl data");
            return;
        }
        
        using var treeNode = ImRaii.TreeNode("Mdl Data");
        if (!treeNode.Success) return;

        foreach (var mtrlName in cachedMdl.GetMaterialNames().Select(x => x.Value))
        {
            using var materialNode = ImRaii.TreeNode(mtrlName);
            if (!materialNode.Success) continue;
            
            if (!mtrlCache.TryGetValue(mtrlName, out var cachedMtrl))
            {
                try
                {
                    var mtrlFile = dataManager.GetFileOrReadFromDisk(mtrlName);
                    if (mtrlFile == null)
                    {
                        mtrlCache[mtrlName] = null;
                    }
                    else
                    {
                        mtrlCache[mtrlName] = new MtrlFile(mtrlFile);
                    }
                }
                catch (Exception e)
                {
                    log.LogError(e, "Failed to load mtrl file");
                    mtrlCache[mtrlName] = null;
                }
                
                cachedMtrl = mtrlCache[mtrlName];
            }
            
            if (cachedMtrl == null)
            {
                ImGui.Text("No cached mtrl data");
                continue;
            }
            
            ImGui.Text($"Shader: {cachedMtrl.GetShaderPackageName()}");
            if (!shpkCache.TryGetValue(cachedMtrl.GetShaderPackageName(), out var cachedShpk))
            {
                try
                {
                    var shpkFile = dataManager.GetFileOrReadFromDisk($"shader/sm5/shpk/{cachedMtrl.GetShaderPackageName()}");
                    if (shpkFile == null)
                    {
                        shpkCache[cachedMtrl.GetShaderPackageName()] = null;
                    }
                    else
                    {
                        shpkCache[cachedMtrl.GetShaderPackageName()] = new ShpkFile(shpkFile);
                    }
                }
                catch (Exception e)
                {
                    log.LogError(e, "Failed to load shpk file");
                    shpkCache[cachedMtrl.GetShaderPackageName()] = null;
                }
                
                cachedShpk = shpkCache[cachedMtrl.GetShaderPackageName()];
            }
            
            if (cachedShpk == null)
            {
                ImGui.Text("No cached shpk data");
                continue;
            }

            // var materialSet = new MaterialSet(cachedMtrl, mtrlName, cachedShpk, cachedMtrl.GetShaderPackageName(), null, null);
            // using (var shpkContentNode = ImRaii.TreeNode("Shpk Constants"))
            // {
            //     if (shpkContentNode.Success)
            //     {
            //         foreach (var constant in materialSet.ShpkConstants)
            //         {
            //             ImGui.Text(Enum.IsDefined(constant.Key)
            //                            ? $"{constant.Key}: {string.Join(", ", constant.Value)}"
            //                            : $"{(uint)constant.Key:X8}: {string.Join(", ", constant.Value)}");
            //         }
            //     }
            // }
            //
            // using (var mtrlContentNode = ImRaii.TreeNode("Mtrl Constants"))
            // {
            //     if (mtrlContentNode.Success)
            //     {
            //         foreach (var constant in materialSet.MtrlConstants)
            //         {
            //             ImGui.Text(Enum.IsDefined(constant.Key)
            //                            ? $"{constant.Key}: {string.Join(", ", constant.Value)}"
            //                            : $"{(uint)constant.Key:X8}: {string.Join(", ", constant.Value)}");
            //         }
            //     }
            // }
            
            TreeNode("Shader Keys", () =>
            {
                foreach (var key in cachedMtrl.ShaderKeys)
                {
                    ImGui.Text($"{(ShaderCategory)key.Category}: {key.Value:X8}");
                }
            });
            
            TreeNode("Color Sets", () =>
            {
                foreach (var key in cachedMtrl.GetColorSetStrings())
                {
                    ImGui.Text(key.Value);
                }
            });
            
            TreeNode("UVColor Sets", () =>
            {
                foreach (var key in cachedMtrl.GetUvColorSetStrings())
                {
                    ImGui.Text(key.Value);
                }
            });
            
            TreeNode("Textures", () =>
            {
                foreach (var key in cachedMtrl.GetTexturePaths())
                {
                    ImGui.Text(key.Value);
                }
            });
            
            TreeNode("Samplers", () =>
            {
                foreach (var key in cachedMtrl.Samplers)
                {
                    ImGui.Text($"[{key.TextureIndex}]{(TextureUsage)key.SamplerId}: {key.Flags}");
                }
            });
            
            TreeNode("ColorTable", () =>
            {
                ImGui.Text($"Has Color Table: {cachedMtrl.HasTable}");
                ImGui.Text($"Has Dye Table: {cachedMtrl.HasDyeTable}");
            });
        }
    }*/
    
    // private void TreeNode(string name, Action action)
    // {
    //     using var node = ImRaii.TreeNode(name);
    //     if (node.Success)
    //     {
    //         action();
    //     }
    // }

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
                    if (materialInfo == null) continue;
                    using var materialNode = ImRaii.TreeNode($"Material: {materialInfo.Path.GamePath}");
                    if (materialNode.Success)
                    {
                        UiUtil.Text($"Material Path: {materialInfo.Path.FullPath}", materialInfo.Path.FullPath);
                        UiUtil.Text($"Game Path: {materialInfo.Path.GamePath}", materialInfo.Path.GamePath);
                        UiUtil.Text($"Shader Path: {materialInfo.Shpk}", materialInfo.Shpk);
                        ImGui.Text($"Texture Count: {materialInfo.Textures.Length}");
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
