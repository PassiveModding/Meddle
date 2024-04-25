using System.Text;
using Meddle.Utils.Files;

namespace Meddle.Utils.Models;

public class Model
{
    public MdlFile File { get; }
    public IReadOnlyDictionary<int, string> Strings { get; }
    
    public Model(MdlFile file)
    {
        File = file;
        Strings = GetStrings();
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
