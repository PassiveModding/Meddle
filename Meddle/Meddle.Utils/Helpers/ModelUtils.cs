using System.Text;
using Meddle.Utils.Files;

namespace Meddle.Utils.Helpers;

public static class ModelUtils
{  
    private static IReadOnlyDictionary<int, string> GetBoneNames(this MdlFile file)
    {
        var strings = file.GetStrings();    
        var boneNames = new Dictionary<int, string>();
        foreach (int offset in file.BoneNameOffsets)
        {
            var name = strings[offset];
            boneNames.Add(offset, name);
        }

        return boneNames;
    }

    public static string[][] GetBoneTables(this MdlFile file)
    {
        var boneNames = file.GetBoneNames().Select(x => x.Value).ToArray();
        var tables = new string[file.BoneTables.Length][];
        for (int i = 0; i < file.BoneTables.Length; i++)
        {
            var table = file.BoneTables[i];
            var names = new string[table.BoneCount];
            for (int j = 0; j < table.BoneCount; j++)
            {
                names[j] = boneNames[table.BoneIndex[j]];
            }
            tables[i] = names;
        }
        
        return tables;
    }
    
    public static Dictionary<int, string> GetMaterialNames(this MdlFile file)
    {
        var strings = file.GetStrings();
        var names = new Dictionary<int, string>();
        for (var i = 0; i < file.MaterialNameOffsets.Length; i++)
        {
            //names[i] = strings[(int)file.MaterialNameOffsets[i]];
            var offset = (int)file.MaterialNameOffsets[i];
            var name = strings[offset];
            names[offset] = name;
        }

        return names;
    }
    
    public static Dictionary<int, string> GetStrings(this MdlFile file)
    {
        var strings = new Dictionary<int, string>();
        var stringReader = new SpanBinaryReader(file.StringTable);
        var offset = 0;
        while (offset < file.StringTable.Length)
        {
            var str = stringReader.ReadByteString(offset);
            strings.Add(offset, Encoding.UTF8.GetString(str));
            offset += str.Length + 1;
        }

        return strings;
    }
}
