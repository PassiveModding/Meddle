using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;

namespace Meddle.Plugin.Utils;

// https://github.com/ktisis-tools/Ktisis/blob/88c1af74f748298d1b1d01135aa58ce0a9530419/Ktisis/Interop/Alloc.cs
internal static class Alloc {
    // Allocations
    private static IntPtr MatrixAlloc;

    // Access
    internal static unsafe Matrix4x4* Matrix; // Align to 16-byte boundary
    internal static unsafe Matrix4x4 GetMatrix(hkQsTransformf* transform) {
        transform->get4x4ColumnMajor((float*)Matrix);
        return *Matrix;
    }
    
    // internal static unsafe void SetMatrix(hkQsTransformf* transform, Matrix4x4 matrix) {
    //     *Matrix = matrix;
    //     transform->set((hkMatrix4f*)Matrix);
    // }

    // Init & dispose
    public static unsafe void Init() {
        // Allocate space for our matrix to be aligned on a 16-byte boundary.
        // This is required due to ffxiv's use of the MOVAPS instruction.
        // Thanks to Fayti1703 for helping with debugging and coming up with this fix.
        MatrixAlloc = Marshal.AllocHGlobal(sizeof(float) * 16 + 16);
        Matrix = (Matrix4x4*)(16 * ((long)(MatrixAlloc + 15) / 16));
    }
    public static void Dispose() {
        Marshal.FreeHGlobal(MatrixAlloc);
    }
}
