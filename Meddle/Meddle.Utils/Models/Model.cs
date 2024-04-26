using System.Text;
using Meddle.Utils.Files;

namespace Meddle.Utils.Models;

public class Model
{
    public MdlFile File { get; }
    public IReadOnlyDictionary<int, string> Strings { get; }
    public string[] BoneNames { get; }
    public string[][] BoneTables { get; }
    
    public Model(MdlFile file)
    {
        File = file;
        Strings = GetStrings();
        BoneNames = File.BoneNameOffsets.Select(x => Strings[(int)x]).ToArray();
        BoneTables = GetBoneTables();
    }

    private IReadOnlyDictionary<int, string> GetBoneNames()
    {
        var boneNames = new Dictionary<int, string>();
        foreach (int offset in File.BoneNameOffsets)
        {
            var name = Strings[offset];
            boneNames.Add(offset, name);
        }

        return boneNames;
    }

    private string[][] GetBoneTables()
    {
        var tables = new string[File.BoneTables.Length][];
        for (int i = 0; i < File.BoneTables.Length; i++)
        {
            var table = File.BoneTables[i];
            var names = new string[table.BoneCount];
            for (int j = 0; j < table.BoneCount; j++)
            {
                names[j] = BoneNames[table.BoneIndex[j]];
            }
            tables[i] = names;
        }
        
        return tables;
    }
    
    private Dictionary<int, string> GetStrings()
    {
        var strings = new Dictionary<int, string>();
        var stringReader = new SpanBinaryReader(File.StringTable);
        var offset = 0;
        while (offset < File.StringTable.Length)
        {
            var str = stringReader.ReadByteString(offset);
            strings.Add(offset, Encoding.UTF8.GetString(str));
            offset += str.Length + 1;
        }

        return strings;
    }
}
