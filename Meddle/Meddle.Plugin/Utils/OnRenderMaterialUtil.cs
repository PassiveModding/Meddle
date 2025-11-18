using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Models;
using Meddle.Utils;
using Meddle.Utils.Helpers;

namespace Meddle.Plugin.Utils;

public class OnRenderMaterialOutput
{
    public string? DecalTexturePath { get; set; }
        
    [JsonIgnore]
    public SkTexture? DecalTexture { get; set; }
    public Vector4? DecalTextureColor { get; set; }
    public List<SkinMaterialTextureInfo> SkinMaterialTextures { get; set; } = new();
}
public record SkinMaterialTextureInfo(string TexturePath, string TexturePathFromMaterial, uint SamplerFlags, uint TargetSamplerCrc);

public static unsafe class OnRenderMaterialUtil
{
    const int MaxSlotCount = (int)HumanModelSlotIndex.Hair;
    const int MaxSkinSlotCount = (int)HumanModelSlotIndex.Feet;
    const int FaceSlot = (int)HumanModelSlotIndex.Face;
    const int HairSlotIndex = (int)HumanModelSlotIndex.Hair;
    
    public static OnRenderMaterialOutput ResolveMonsterOnRenderMaterial(Pointer<Monster> monster, Pointer<Model> model, uint materialIndex)
    {
        var output = new OnRenderMaterialOutput();
        if (monster.Value->Decal != null)
        {
            var decalTexturePtr = monster.Value->Decal;
            var decalColorCBuffer = CharacterUtility.Instance()->FreeCompanyCrestColorTypedCBuffer.TryGetIndex(0);
            ApplyTextureAndConstantBuffer(ref output, decalTexturePtr, decalColorCBuffer);
        }
        else
        {
            var transparentTexture = GetTransparentTexture();
            var decalColorCBuffer = CharacterUtility.Instance()->FreeCompanyCrestColorTypedCBuffer.TryGetIndex(0);
            ApplyTextureAndConstantBuffer(ref output, transparentTexture.Value, decalColorCBuffer);
        }

        return output;
    }
    
    public static OnRenderMaterialOutput ResolveWeaponOnRenderMaterial(Pointer<Weapon> weapon, Pointer<Model> model, uint materialIndex)
    {
        var output = new OnRenderMaterialOutput();
        var slotIndex = model.Value->SlotIndex;
        if (ShouldApplyFreeCompanyCrest(weapon, slotIndex))
        {
            ApplyFreeCompanyCrest(ref output, weapon.Value->FreeCompanyCrest);
            return output;
        }

        if (weapon.Value->Decal != null)
        {
            var decalTexturePtr = weapon.Value->Decal;
            var decalColorCBuffer = CharacterUtility.Instance()->FreeCompanyCrestColorTypedCBuffer.TryGetIndex(0);
            ApplyTextureAndConstantBuffer(ref output, decalTexturePtr, decalColorCBuffer);
        }
        else
        {
            var transparentTexture = GetTransparentTexture();
            var decalColorCBuffer = CharacterUtility.Instance()->FreeCompanyCrestColorTypedCBuffer.TryGetIndex(0);
            ApplyTextureAndConstantBuffer(ref output, transparentTexture.Value, decalColorCBuffer);
        }

        return output;
    }
    
    public static OnRenderMaterialOutput ResolveDemihumanOnRenderMaterial(Pointer<Demihuman> demihuman, Pointer<Model> model, uint materialIndex)
    {
        var output = new OnRenderMaterialOutput();
        var slotIndex = model.Value->SlotIndex;
        if (ShouldApplyFreeCompanyCrest(demihuman, slotIndex))
        {
            ApplyFreeCompanyCrest(ref output, demihuman.Value->FreeCompanyCrest);
            return output;
        }
        
        var slotDecal = demihuman.Value->SlotDecals.Length > slotIndex
                            ? demihuman.Value->SlotDecals[(int)slotIndex]
                            : null;
        if (slotDecal != null)
        {
            var decalTexturePtr = slotDecal.Value;
            var decalColorCBuffer = CharacterUtility.Instance()->FreeCompanyCrestColorTypedCBuffer.TryGetIndex(0);
            ApplyTextureAndConstantBuffer(ref output, decalTexturePtr, decalColorCBuffer);
        }
        else
        {
            var transparentTexture = GetTransparentTexture();
            var decalColorCBuffer = CharacterUtility.Instance()->FreeCompanyCrestColorTypedCBuffer.TryGetIndex(0);
            ApplyTextureAndConstantBuffer(ref output, transparentTexture.Value, decalColorCBuffer);
        }

        return output;
    }

    public static OnRenderMaterialOutput ResolveHumanOnRenderMaterial(Pointer<Human> human, Pointer<Model> model, uint materialIndex)
    {
        var slotIndex = model.Value->SlotIndex;
        var materialAtIndex = model.Value->MaterialsSpan[(int)materialIndex];
        var output = new OnRenderMaterialOutput();
        if (slotIndex < MaxSlotCount)
        {
            if (IsLegacyBodyDecalMaterial(materialAtIndex))
            {
                ApplyLegacyBodyDecal(ref output, human);
            }
            else if (ShouldApplyFreeCompanyCrest(human, slotIndex))
            {
                ApplyFreeCompanyCrest(ref output, human.Value->FreeCompanyCrest);
            }
            else
            {
                ApplySlotDecal(ref output, human, slotIndex);
            }

            if (slotIndex <= MaxSkinSlotCount)
            {
                ApplySkinMaterialTextures(ref output, human, materialAtIndex, slotIndex);
            }

            return output;
        }

        if (slotIndex == FaceSlot)
        {
            var decal = human.Value->Decal;
            if (decal->LoadState <= 7)
            {
                // decal loaded
                var decalColorCBuffer = human.Value->DecalColorTypedCBuffer.TryGetIndex(0);
                ApplyTextureAndConstantBuffer(ref output, decal, decalColorCBuffer);
            }
            else
            {
                // fallback to transparent
                ApplyTextureAndConstantBuffer(ref output, null, null);
            }
            
            return output;
        }

        if (slotIndex == HairSlotIndex)
        {
            // return HandleHairSlot(parameters);
            // hair sets some flags on the output but no texture or color is applied
            return output;
        }

        return output;
    }

    private static void ApplySkinMaterialTextures(ref OnRenderMaterialOutput renderMaterialOutput, Pointer<Human> human, Pointer<Material> materialAtIndex, uint slotIndex)
    {
        var skinMaterialHandle = human.Value->SlotSkinMaterials.Length > slotIndex
                                     ? human.Value->SlotSkinMaterials[(int)slotIndex]
                                     : null;

        if (skinMaterialHandle == null || skinMaterialHandle.Value == null)
        {
            return;
        }

        var skinMaterial = skinMaterialHandle.Value->Material;
        if (skinMaterial == null)
        {
            return;
        }

        /*
            unk* skinCBuffer = *(&human->field_B98 + slotIndex);
            if (!skinCBuffer) {
                return;
            }
         */

        var slotShpk = materialAtIndex.Value->MaterialResourceHandle->ShpkName;
        if (slotShpk != "characterstockings.shpk")
        {
            return;
        }

        CopySkinMaterialTextures(ref renderMaterialOutput, skinMaterial, materialAtIndex);
        ApplyLegacyBodyDecal(ref renderMaterialOutput, human);
    }

    private static void CopySkinMaterialTextures(ref OnRenderMaterialOutput renderMaterialOutput, Pointer<Material> skinMaterial, Pointer<Material> materialAtIndex)
    {
        var cu = (CharacterUtilityExtension*)CharacterUtility.Instance();

        var skinMaterialHandle = skinMaterial.Value->MaterialResourceHandle;
        if (skinMaterialHandle == null)
        {
            return;
        }

        var displayMaterialTextures = materialAtIndex.Value->MaterialResourceHandle->ShaderPackageResourceHandle->ShaderPackage->TexturesSpan.ToArray();
        for (var i = 0; i < skinMaterial.Value->TexturesSpan.Length; i++)
        {
            var entry = skinMaterial.Value->TexturesSpan[i];
            var textureId = entry.Id;
            uint targetSlot;
            if (cu->Field540_Low == textureId)
            {
                targetSlot = cu->Field548_High;
            }
            else if (cu->Field540_High == textureId)
            {
                targetSlot = cu->Field550_Low;
            }
            else if (cu->Field548_Low == textureId)
            {
                targetSlot = cu->Field550_High;
            }
            else
            {
                continue; // Not a texture we care about
            }

            string? path = null;
            string? pathFromMaterial = skinMaterialHandle->TextureCount > i ? skinMaterialHandle->TexturePath(i) : null;
            if (entry.Texture != null && entry.Texture->Texture != null)
            {
                path = entry.Texture->FileName.ToString();
            }

            uint samplerFlags = entry.SamplerFlags; // if tex was out of index; dword_14271F638;
            path ??= GetTransparentTexture().Value->FileName.ToString();
            pathFromMaterial ??= GetTransparentTexture().Value->FileName.ToString();

            if (displayMaterialTextures.Any(x => x.Id == targetSlot))
            {
                var targetSlotMatch = displayMaterialTextures.First(x => x.Id == targetSlot);
                var targetSlotCrc = targetSlotMatch.CRC;
                renderMaterialOutput.SkinMaterialTextures.Add(new SkinMaterialTextureInfo(path, pathFromMaterial, samplerFlags, targetSlotCrc));
            }
        }
    }


    [StructLayout(LayoutKind.Explicit)]
    public struct CharacterUtilityExtension
    {
        [FieldOffset(0x540)]
        public uint Field540_Low;

        [FieldOffset(0x544)]
        public uint Field540_High;

        [FieldOffset(0x548)]
        public uint Field548_Low;

        [FieldOffset(0x54C)]
        public uint Field548_High;

        [FieldOffset(0x550)]
        public uint Field550_Low;

        [FieldOffset(0x554)]
        public uint Field550_High;
    }
    
    private static void ApplyFreeCompanyCrest(ref OnRenderMaterialOutput renderMaterialOutput, Texture* crestPtr)
    {
        string? texturePath = null;
        if (crestPtr == null)
        {
            crestPtr = GetTransparentTexture().Value->Texture;
            texturePath = GetTransparentTexture().Value->FileName.ToString();
        }

        // need to resolve the actual crest here since it's computed at runtime
        var crestResource = DxHelper.ExportTextureResource(crestPtr);

        var crestColorCBuffer = CharacterUtility.Instance()->FreeCompanyCrestColorTypedCBuffer.TryGetIndex(0);
        renderMaterialOutput.DecalTexture = crestResource.Resource.ToTexture();
        renderMaterialOutput.DecalTextureColor = crestColorCBuffer;
        renderMaterialOutput.DecalTexturePath = texturePath; // crest has no path, keeping it null.
    }

    private static Pointer<TextureResourceHandle> GetTransparentTexture()
    {
        var characterUtility = CharacterUtility.Instance();
        var handle = characterUtility->ResourceHandles[79];
        return (TextureResourceHandle*)handle.Value;
    }

    private static void ApplyLegacyBodyDecal(ref OnRenderMaterialOutput renderMaterialOutput, Pointer<Human> human)
    {
        var decalTexturePtr = human.Value->LegacyBodyDecal;
        var decalColorCBuffer = CharacterUtility.Instance()->LegacyBodyDecalColorTypedCBuffer.TryGetIndex(0);
        ApplyTextureAndConstantBuffer(ref renderMaterialOutput, decalTexturePtr, decalColorCBuffer);
    }

    private static Vector4? TryGetIndex(this ConstantBufferPointer<FFXIVClientStructs.FFXIV.Common.Math.Vector4> buffer, uint index)
    {
        var buf = buffer.TryGetBuffer();
        if (buf.Length > (int)index)
        {
            return buf[(int)index];
        }

        return null;
    }

    private static void ApplySlotDecal(ref OnRenderMaterialOutput renderMaterialOutput, Pointer<Human> human, uint slotIndex)
    {
        var slotDecal = human.Value->SlotDecals.Length > slotIndex
                            ? human.Value->SlotDecals[(int)slotIndex]
                            : null;

        TextureResourceHandle* decalTexture;
        Vector4? decalColor = CharacterUtility.Instance()->FreeCompanyCrestColorTypedCBuffer.TryGetIndex(0);
        if (slotDecal != null)
        {
            decalTexture = slotDecal.Value;
        }
        else
        {
            decalTexture = GetTransparentTexture().Value;
        }

        ApplyTextureAndConstantBuffer(ref renderMaterialOutput, decalTexture, decalColor);
    }

    private static void ApplyTextureAndConstantBuffer(ref OnRenderMaterialOutput renderMaterialOutput, TextureResourceHandle* textureResourceHandle, Vector4? buffer)
    {
        if (textureResourceHandle == null)
        {
            renderMaterialOutput.DecalTexturePath = GetTransparentTexture().Value->FileName.ToString();
        }
        else
        {
            renderMaterialOutput.DecalTexturePath = textureResourceHandle->FileName.ToString();
        }

        if (buffer == null)
        {
            renderMaterialOutput.DecalTextureColor = CharacterUtility.Instance()->FreeCompanyCrestColorTypedCBuffer.TryGetIndex(0);
        }
        else
        {
            renderMaterialOutput.DecalTextureColor = buffer.Value;
        }
    }

    private static bool IsLegacyBodyDecalMaterial(Pointer<Material> materialPtr)
    {
        if (materialPtr == null || materialPtr.Value == null)
        {
            return false;
        }

        var material = materialPtr.Value;
        var materialPath = material->MaterialResourceHandle->ShpkName.ToString();
        if (materialPath == "skin.shpk")
        {
            return true;
        }

        return false;
    }

    private static bool ShouldApplyFreeCompanyCrest(Pointer<Human> ptr, uint slotIdx)
    {
        return ShouldApplyFreeCompanyCrest(ptr.Value->FreeCompanyCrest, slotIdx, ptr.Value->SlotFreeCompanyCrestBitfield);
    }
    
    private static bool ShouldApplyFreeCompanyCrest(Pointer<Weapon> ptr, uint slotIdx)
    {
        return ShouldApplyFreeCompanyCrest(ptr.Value->FreeCompanyCrest, slotIdx, ptr.Value->SlotFreeCompanyCrestBitfield);
    }
    
    private static bool ShouldApplyFreeCompanyCrest(Pointer<Demihuman> ptr, uint slotIdx)
    {
        return ShouldApplyFreeCompanyCrest(ptr.Value->FreeCompanyCrest, slotIdx, ptr.Value->SlotFreeCompanyCrestBitfield);
    }
    
    private static bool ShouldApplyFreeCompanyCrest(Texture* crestPtr, uint slotIdx, uint slotBitfield)
    {
        if (crestPtr == null)
        {
            return false;
        }

        return (slotBitfield >> (int)slotIdx & 1) != 0;
    }
}
