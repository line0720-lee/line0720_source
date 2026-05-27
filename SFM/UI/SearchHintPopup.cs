using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SFM.UI;

/// <summary>
/// 검색어 입력 방법을 표 형태로 보여주는 비활성 팝업 창.
/// 마우스 호버 시 표시되며, 포커스를 빼앗지 않는다.
/// </summary>
internal sealed class SearchHintPopup : Form
{
    // ── 힌트 데이터 ──────────────────────────────────────────────
    private static readonly (string Keyword, string Pattern, string Result)[] Rows =
    [
        ("abc",      "abc%",     "abc로 시작하는 파일  (abc.txt, abc가나.doc…)"),
        ("*abc",     "%abc%",    "abc를 포함하는 파일  (가나abc.txt, 테스트abc.docx…)"),
        ("abc*",     "abc%",     "abc로 시작하는 파일"),
        ("*abc*",    "%abc%",    "abc를 포함하는 파일"),
        ("*.txt",    "%.txt",    "확장자가 .txt인 파일"),
        ("abc*.txt", "abc%.txt", "abc로 시작하는 .txt 파일"),
    ];

    // ── 레이아웃 상수 (원본 × 1.5) ───────────────────────────────
    private const int PadX   = 18;
    private const int PadY   = 14;
    private const int TitleH = 38;
    private const int HdrH   = 34;
    private const int RowH   = 32;
    private const int Cw1    = 138;   // 검색어 열
    private const int Cw2    = 138;   // LIKE 패턴 열
    private const int Cw3    = 444;   // 결과 열
    private const int SepW   = 1;

    private static readonly int TableW = Cw1 + SepW + Cw2 + SepW + Cw3;
    private static readonly int FormW  = PadX * 2 + TableW;
    private static readonly int FormH  = PadY + TitleH + HdrH + Rows.Length * RowH + PadY;

    private bool _isDark;

    public SearchHintPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar   = false;
        StartPosition   = FormStartPosition.Manual;
        TopMost         = false;
        ClientSize      = new Size(FormW, FormH);
        BackColor       = Color.White;

        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint  |
            ControlStyles.UserPaint, true);
    }

    // ── 포커스를 빼앗지 않는 팝업 ────────────────────────────────
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return cp;
        }
    }

    // ── 테마 변경 ────────────────────────────────────────────────
    public void SetDark(bool isDark)
    {
        if (_isDark == isDark) return;
        _isDark   = isDark;
        BackColor = isDark ? Color.FromArgb(35, 35, 52) : Color.White;
        Invalidate();
    }

    // ── 위치 계산 후 표시 ─────────────────────────────────────────
    /// <summary>anchor 컨트롤 바로 아래에 팝업을 표시한다.</summary>
    public void ShowBelow(Control anchor)
    {
        var pt     = anchor.PointToScreen(new Point(0, anchor.Height + 2));
        var screen = Screen.FromControl(anchor).WorkingArea;

        int x = pt.X - Width / 2 + anchor.Width / 2;
        int y = pt.Y;

        if (x + Width  > screen.Right)  x = screen.Right - Width - 4;
        if (x          < screen.Left)   x = screen.Left  + 4;
        if (y + Height > screen.Bottom) y = anchor.PointToScreen(new Point(0, -Height - 4)).Y;

        Location = new Point(x, y);
        if (!Visible) Show();
    }

    // ── 그리기 ───────────────────────────────────────────────────
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // 색상 팔레트
        var cBg      = _isDark ? Color.FromArgb(35, 35, 52)   : Color.White;
        var cBorder  = _isDark ? Color.FromArgb(68, 70, 100)  : Color.FromArgb(193, 213, 242);
        var cTitle   = _isDark ? Color.FromArgb(155, 195, 245): Color.FromArgb(13, 71, 161);
        var cHdr     = Color.FromArgb(13, 71, 161);
        var cRowEven = _isDark ? Color.FromArgb(38, 38, 56)   : Color.White;
        var cRowOdd  = _isDark ? Color.FromArgb(44, 44, 63)   : Color.FromArgb(244, 248, 255);
        var cCode    = _isDark ? Color.FromArgb(95, 195, 255) : Color.FromArgb(13, 71, 161);
        var cText    = _isDark ? Color.FromArgb(218, 220, 234): Color.FromArgb(26, 32, 44);
        var cSep     = _isDark ? Color.FromArgb(56, 58, 82)   : Color.FromArgb(208, 222, 242);

        var rc = new Rectangle(0, 0, Width - 1, Height - 1);

        // 배경 (둥근 모서리 + 그림자 흉내)
        using var bgPath = RoundRect(rc, 8);
        using (var bgBr = new SolidBrush(cBg))
            g.FillPath(bgBr, bgPath);
        using (var pen = new Pen(cBorder, 1.2f))
            g.DrawPath(pen, bgPath);

        // 제목
        using var titleFont = new Font("Segoe UI", 10F);
        using (var titleBr = new SolidBrush(cTitle))
            g.DrawString("검색어 입력 방법  (와일드카드 지원)", titleFont, titleBr,
                         new PointF(PadX + 2, PadY + 4));

        // 테이블 기준 좌표
        int tx = PadX;
        int ty = PadY + TitleH;
        int x1 = tx;                              // 검색어 열 시작
        int x2 = tx + Cw1 + SepW;                // LIKE 패턴 열 시작
        int x3 = tx + Cw1 + SepW + Cw2 + SepW;  // 결과 열 시작

        // 헤더 행
        using (var hdrBr = new SolidBrush(cHdr))
            g.FillRectangle(hdrBr, tx, ty, TableW, HdrH);

        using var hdrFont = new Font("Segoe UI", 10F);
        var sfC = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            Trimming      = StringTrimming.EllipsisCharacter,
            FormatFlags   = StringFormatFlags.NoWrap
        };
        g.DrawString("검색어",    hdrFont, Brushes.White, new RectangleF(x1+4, ty, Cw1-8, HdrH), sfC);
        g.DrawString("LIKE 패턴", hdrFont, Brushes.White, new RectangleF(x2+4, ty, Cw2-8, HdrH), sfC);
        g.DrawString("결과",      hdrFont, Brushes.White, new RectangleF(x3+4, ty, Cw3-8, HdrH), sfC);

        // 데이터 행
        using var normFont = new Font("Segoe UI", 10F);
        using var codeFont = new Font("Consolas", 10F);
        using var codeBr   = new SolidBrush(cCode);
        using var textBr   = new SolidBrush(cText);
        using var sepPen   = new Pen(cSep, 1);

        for (int i = 0; i < Rows.Length; i++)
        {
            int ry = ty + HdrH + i * RowH;

            using (var rowBr = new SolidBrush(i % 2 == 0 ? cRowEven : cRowOdd))
                g.FillRectangle(rowBr, tx, ry, TableW, RowH);

            // 행 하단 구분선 (마지막 행 제외)
            if (i < Rows.Length - 1)
                g.DrawLine(sepPen, tx, ry + RowH, tx + TableW, ry + RowH);

            // 열 세로 구분선
            g.DrawLine(sepPen, x2 - SepW, ry, x2 - SepW, ry + RowH);
            g.DrawLine(sepPen, x3 - SepW, ry, x3 - SepW, ry + RowH);

            // 셀 데이터
            g.DrawString(Rows[i].Keyword, codeFont, codeBr, new RectangleF(x1+4, ry, Cw1-8, RowH), sfC);
            g.DrawString(Rows[i].Pattern, codeFont, codeBr, new RectangleF(x2+4, ry, Cw2-8, RowH), sfC);
            g.DrawString(Rows[i].Result,  normFont, textBr, new RectangleF(x3+4, ry, Cw3-8, RowH), sfC);
        }
        sfC.Dispose();

        // 테이블 외곽선
        g.DrawRectangle(sepPen, tx, ty, TableW, HdrH + Rows.Length * RowH);
    }

    private static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        path.AddArc(r.X,               r.Y,                radius * 2, radius * 2, 180, 90);
        path.AddArc(r.Right - radius*2, r.Y,                radius * 2, radius * 2, 270, 90);
        path.AddArc(r.Right - radius*2, r.Bottom - radius*2, radius * 2, radius * 2,   0, 90);
        path.AddArc(r.X,               r.Bottom - radius*2, radius * 2, radius * 2,  90, 90);
        path.CloseFigure();
        return path;
    }
}
