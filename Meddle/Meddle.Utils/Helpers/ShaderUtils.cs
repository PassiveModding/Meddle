using System.Runtime.InteropServices;

namespace Meddle.Utils.Helpers;

public static class ShaderUtils
{
    [Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3DBlob
    {
        [PreserveSig]
        public unsafe void* GetBufferPointer();

        [PreserveSig]
        public UIntPtr GetBufferSize();
    }
    
    [Flags]
    public enum DisassembleFlags : uint
    {
        EnableColorCode            = 1,
        EnableDefaultValuePrints   = 2,
        EnableInstructionNumbering = 4,
        EnableInstructionCycle     = 8,
        DisableDebugInfo           = 16,
        EnableInstructionOffset    = 32,
        InstructionOnly            = 64,
        PrintHexLiterals           = 128,
    }
    
    public static unsafe string Disassemble(ReadOnlySpan<byte> blob, DisassembleFlags flags = 0, string comments = "")
    {
        ID3DBlob? disassembly = null;
        try
        {
            fixed (byte* pSrcData = blob)
            {
                var hr = D3DDisassemble(pSrcData, new UIntPtr((uint)blob.Length), (uint)flags, comments, out disassembly);
                Marshal.ThrowExceptionForHR(hr);
            }
            
            // Get the disassembly
            var buffer = (byte*)disassembly!.GetBufferPointer();
            var size = disassembly.GetBufferSize();
            return Marshal.PtrToStringAnsi(new IntPtr(buffer), (int)size);
        }
        finally
        {
            if (disassembly != null)
                Marshal.FinalReleaseComObject(disassembly);
        }
    }
    
    [PreserveSig]
    [DllImport("D3DCompiler_47.dll")]
    private static extern unsafe int D3DDisassemble(
        [In] byte* pSrcData,
        [In] UIntPtr srcDataSize,
        uint flags,
        [MarshalAs(UnmanagedType.LPStr)] string szComments,
        out ID3DBlob? ppDisassembly);
}
