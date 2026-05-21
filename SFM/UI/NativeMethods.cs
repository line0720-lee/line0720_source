using System.Runtime.InteropServices;

namespace SFM.UI;

/// <summary>SHFileOperation 등 Shell32 P/Invoke.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NativeShFileOp
{
    public IntPtr hwnd;
    public int    wFunc;
    public string pFrom;
    public string pTo;
    public short  fFlags;
    [MarshalAs(UnmanagedType.Bool)]
    public bool   fAnyOperationsAborted;
    public IntPtr hNameMappings;
    public string lpszProgressTitle;
}

internal static class NativeMethods
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHFileOperation(ref NativeShFileOp lpFileOp);
}
