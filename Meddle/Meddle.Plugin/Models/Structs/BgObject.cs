using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;

namespace Meddle.Plugin.Models.Structs;

[StructLayout(LayoutKind.Explicit, Size = 0xD0)]
public unsafe struct BgObject
{
    [FieldOffset(0x00)] public FFXIVClientStructs.FFXIV.Client.Graphics.Scene.BgObject Base;
    [FieldOffset(0x90)] public ModelResourceHandle* ModelResourceHandle;
    [FieldOffset(0xA8)] public BgObjectAdditionalData* BGChangeData;
    
    public ModelResourceHandleData GetModelResourceHandleData()
    {
        if (ModelResourceHandle == null)
            return default;
        
        return new ModelResourceHandleData(ModelResourceHandle->ModelData);
    }
    public (int MaterialIndex, Pointer<MaterialResourceHandle> ResourceHandle)? GetBgChangeMaterial()
    {
        if (BGChangeData == null || ModelResourceHandle == null)
            return null;
        
        var modelData = GetModelResourceHandleData();
        if (modelData.ModelHeader.MaterialCount == 0)
            return null;
        
        var flags3Value = modelData.ModelHeader.Flags3;
        if ((flags3Value & 0x2) != 0 & modelData.ModelHeader.BGChangeMaterialIndex < modelData.ModelHeader.MaterialCount)
        {
            if (BGChangeData->MaterialResourceHandle == null)
            {
                return null;
            }

            Pointer<MaterialResourceHandle> handle = BGChangeData->MaterialResourceHandle;
            return (modelData.ModelHeader.BGChangeMaterialIndex, handle);
        }
        
        return null;
    }
}

[StructLayout(LayoutKind.Explicit, Size = 0x38)]
public unsafe struct BgObjectAdditionalData
{
    [FieldOffset(0x18)] public MaterialResourceHandle* MaterialResourceHandle;
}
