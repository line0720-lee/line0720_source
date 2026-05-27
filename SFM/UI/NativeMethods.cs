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

    // 창 메시지 전송 (ListView 더블 버퍼링 등에 사용)
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    // ── Shell 아이콘 플래그 ───────────────────────────────────
    internal const uint SHGFI_ICON              = 0x0000_0100;
    internal const uint SHGFI_SMALLICON         = 0x0000_0001;
    internal const uint SHGFI_USEFILEATTRIBUTES = 0x0000_0010;
    internal const uint FILE_ATTRIBUTE_NORMAL   = 0x0000_0080;

    // ── ListView 더블 버퍼링 ──────────────────────────────────
    internal const int LVM_SETEXTENDEDLISTVIEWSTYLE = 0x1036;
    internal const int LVS_EX_DOUBLEBUFFER          = 0x0001_0000;
}

/// <summary>
/// VirtualMode ListView 확장 — 스크롤이 하단에 근접하면 <see cref="NearBottom"/> 이벤트를 발생시킨다.
/// 무한 스크롤(200개 단위 추가 로드)에 사용한다.
/// </summary>
internal sealed class ExtListView : ListView
{
    /// <summary>표시된 마지막 아이템이 전체 목록 하단 100개 이내에 들어오면 발생한다.</summary>
    public event EventHandler? NearBottom;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // WM_VSCROLL = 0x0115  스크롤바 / 키보드 방향키
        // WM_MOUSEWHEEL = 0x020A  마우스 휠
        // WM_KEYDOWN = 0x0100  Page Down / End 등
        if (m.Msg is 0x0115 or 0x020A or 0x0100)
            CheckNearBottom();
    }

    private void CheckNearBottom()
    {
        if (VirtualListSize == 0 || TopItem == null) return;
        // TopItem.Index + 100 ≥ VirtualListSize → 하단 100개 이내 → 추가 로드 신호
        if (TopItem.Index + 100 >= VirtualListSize)
            NearBottom?.Invoke(this, EventArgs.Empty);
    }
}
