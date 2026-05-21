using System.Runtime.InteropServices;

namespace SFM.UI;

// ── SHFileOperation (휴지통 삭제) ────────────────────────────────
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

// ── SHGetFileInfo (셸 아이콘 추출) ──────────────────────────────
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NativeSHFILEINFO
{
    public IntPtr hIcon;
    public int    iIcon;
    public uint   dwAttributes;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string szDisplayName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
    public string szTypeName;
}

internal static class NativeMethods
{
    // 휴지통 이동
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHFileOperation(ref NativeShFileOp lpFileOp);

    // 파일 아이콘 추출 (확장자 기반)
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes,
        ref NativeSHFILEINFO psfi, int cbFileInfo, uint uFlags);

    // HICON 해제
    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    internal const uint SHGFI_ICON              = 0x0000_0100;
    internal const uint SHGFI_SMALLICON         = 0x0000_0001;
    internal const uint SHGFI_USEFILEATTRIBUTES = 0x0000_0010;
    internal const uint FILE_ATTRIBUTE_NORMAL   = 0x0000_0080;
}
