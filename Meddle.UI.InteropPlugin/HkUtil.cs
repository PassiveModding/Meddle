using System.Text;
using FFXIVClientStructs.Havok;

namespace Meddle.UI.InteropPlugin;

public static class HkUtil
{
    private static unsafe hkResource* Read(string filePath)
    {
        var path                = Encoding.UTF8.GetBytes(filePath);
        var builtinTypeRegistry = hkBuiltinTypeRegistry.Instance();

        var loadOptions = stackalloc hkSerializeUtil.LoadOptions[1];
        loadOptions->Flags = new hkFlags<hkSerializeUtil.LoadOptionBits, int> { Storage = (int)hkSerializeUtil.LoadOptionBits.Default };
        loadOptions->ClassNameRegistry = builtinTypeRegistry->GetClassNameRegistry();
        loadOptions->TypeInfoRegistry = builtinTypeRegistry->GetTypeInfoRegistry();

        return hkSerializeUtil.LoadFromFile(path, null, loadOptions);
    }
    
    public static unsafe string HkxToXml(string pathToHkx)
    {
        const hkSerializeUtil.SaveOptionBits options = hkSerializeUtil.SaveOptionBits.SerializeIgnoredMembers
                                                       | hkSerializeUtil.SaveOptionBits.TextFormat
                                                       | hkSerializeUtil.SaveOptionBits.WriteAttributes;
        
        var resource = Read(pathToHkx);

        if (resource == null)
            throw new Exception("Failed to read havok file.");

        var file = Write(resource, options);
        file.Close();

        var contents = File.ReadAllText(file.Name);
        File.Delete(file.Name);

        return contents;
    }
    
    private static unsafe FileStream Write(
        hkResource* resource,
        hkSerializeUtil.SaveOptionBits optionBits
    )
    {
        var tempFileName = Path.GetTempFileName();
        var tempFile = Path.ChangeExtension(tempFileName, ".hkx");
        var path     = Encoding.UTF8.GetBytes(tempFile);
        var oStream  = new hkOstream();
        oStream.Ctor(path);

        var result = stackalloc hkResult[1];

        var saveOptions = new hkSerializeUtil.SaveOptions()
        {
            Flags = new hkFlags<hkSerializeUtil.SaveOptionBits, int> { Storage = (int)optionBits },
        };

        var builtinTypeRegistry = hkBuiltinTypeRegistry.Instance();
        var classNameRegistry   = builtinTypeRegistry->GetClassNameRegistry();
        var typeInfoRegistry    = builtinTypeRegistry->GetTypeInfoRegistry();

        try
        {
            const string name = "hkRootLevelContainer";

            var resourcePtr = (hkRootLevelContainer*)resource->GetContentsPointer(name, typeInfoRegistry);
            if (resourcePtr == null)
                throw new Exception("Failed to retrieve havok root level container resource.");

            var hkRootLevelContainerClass = classNameRegistry->GetClassByName(name);
            if (hkRootLevelContainerClass == null)
                throw new Exception("Failed to retrieve havok root level container type.");

            hkSerializeUtil.Save(result, resourcePtr, hkRootLevelContainerClass, oStream.StreamWriter.ptr, saveOptions);
        }
        finally
        {
            oStream.Dtor();
        }

        if (result->Result == hkResult.hkResultEnum.Failure)
            throw new Exception("Failed to serialize havok file.");

        return new FileStream(tempFile, FileMode.Open);
    }
}
