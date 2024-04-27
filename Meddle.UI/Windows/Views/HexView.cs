using System.Text;
using ImGuiNET;

namespace Meddle.UI.Windows.Views;

public class HexView(ReadOnlySpan<byte> data)
{
    private readonly byte[] data = data.ToArray();

    private int offset;
    private int linesToDraw = 100;
    public const int SliceLength = 16;
    public void DrawHexDump()
    {
        var span = this.data.AsSpan();
        var sliceCount = span.Length / SliceLength;
        var maxOffset = Math.Max(sliceCount - linesToDraw, 0);
        ImGui.SliderInt("Offset", ref offset, 0, maxOffset);
        // lines to draw slider
        ImGui.SliderInt("Lines to Draw", ref linesToDraw, 100, 1000);
        
        for (var i = offset; i < offset + linesToDraw; i++)
        {
            if (i >= sliceCount)
            {
                break;
            }
            var slice = span.Slice(i * SliceLength, SliceLength);
            var sliceStr = new StringBuilder();
            var textStr = new StringBuilder();
            for (var j = 0; j < slice.Length; j++)
            {
                var element = slice[j];
                sliceStr.Append(element.ToString("X2"));
                if (j < slice.Length - 1)
                {
                    sliceStr.Append(" ");
                }
                
                var c = (char)element;
                if (char.IsLetterOrDigit(c) || char.IsPunctuation(c))
                {
                    textStr.Append(c);
                }
                else if (char.IsWhiteSpace(c))
                {
                    textStr.Append(' ');
                }
                else
                {
                    textStr.Append('.');
                }
                textStr.Append(' ');
            }
            
            var sliceOffset = i * SliceLength;
            ImGui.TextUnformatted($"{sliceOffset:X8} | {sliceStr} | {textStr}");
            
        }
    }
}
