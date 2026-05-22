using System.Drawing.Drawing2D;

namespace SFM.UI;

/// <summary>SFM 정보 다이얼로그 — 커스텀 디자인 적용.</summary>
internal sealed class AboutForm : Form
{
    // ── 색상 팔레트 ────────────────────────────────────────────
    private static readonly Color CPrimary  = Color.FromArgb(13,  71, 161);   // #0D47A1
    private static readonly Color CAccent   = Color.FromArgb(25, 118, 210);   // #1976D2
    private static readonly Color CBg       = Color.FromArgb(245, 246, 250);
    private static readonly Color CSurface  = Color.White;
    private static readonly Color CText     = Color.FromArgb(26,  32,  44);
    private static readonly Color CSub      = Color.FromArgb(100, 116, 139);
    private static readonly Color CBorder   = Color.FromArgb(203, 213, 225);

    public AboutForm()
    {
        // ── 폼 기본 설정 ──────────────────────────────────────
        Text            = "SFM 정보";
        Size            = new Size(480, 580);
        MinimumSize     = Size;
        MaximumSize     = Size;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterParent;
        BackColor       = CBg;
        Font            = new Font("Segoe UI", 9F);

        BuildUI();
    }

    private void BuildUI()
    {
        // ── 헤더 패널 (파란 배경) ─────────────────────────────
        var pnlHeader = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 120,
            BackColor = CPrimary,
        };
        pnlHeader.Paint += PaintHeader;

        // ── 본문 패널 ─────────────────────────────────────────
        var pnlBody = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = CSurface,
            Padding   = new Padding(28, 20, 28, 20),
        };

        // 설명 라벨
        var lblDesc = new Label
        {
            Text      = "내 컴퓨터 파일 전문 검색 도구\n파일명 + 내용 인덱싱 · 한국어 검색 지원\nAI 기반 자동 인덱싱 탑재",
            Font      = new Font("Segoe UI", 9.5F),
            ForeColor = CText,
            AutoSize  = false,
            Dock      = DockStyle.Top,
            Height    = 74,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        // 구분선
        var sep1 = MakeSeparator();

        // 단축키 제목
        var lblShortcutTitle = new Label
        {
            Text      = "단축키",
            Font      = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = CAccent,
            AutoSize  = false,
            Dock      = DockStyle.Top,
            Height    = 30,
            TextAlign = ContentAlignment.BottomLeft,
        };

        // 단축키 목록 패널
        var pnlKeys = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 128,
            BackColor = Color.FromArgb(240, 244, 252),
            Padding   = new Padding(12, 10, 12, 10),
        };
        pnlKeys.Paint += (s, e) =>
        {
            using var pen = new Pen(CBorder);
            var rc = ((Panel)s!).ClientRectangle;
            e.Graphics.DrawRectangle(pen, 0, 0, rc.Width - 1, rc.Height - 1);
        };

        string[] shortcuts =
        [
            "F4", "내용 검색 토글",
            "F5", "전체 재인덱싱",
            "Enter", "파일 열기",
            "Esc", "검색어 지우기",
        ];

        int ky = 10;
        for (int i = 0; i < shortcuts.Length; i += 2)
        {
            var badge = new Label
            {
                Text      = shortcuts[i],
                Font      = new Font("Consolas", 8.5F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = CAccent,
                AutoSize  = false,
                Location  = new Point(12, ky),
                Size      = new Size(52, 22),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            var desc = new Label
            {
                Text      = shortcuts[i + 1],
                Font      = new Font("Segoe UI", 9F),
                ForeColor = CText,
                AutoSize  = false,
                Location  = new Point(72, ky),
                Size      = new Size(280, 22),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            pnlKeys.Controls.AddRange([badge, desc]);
            ky += 28;
        }

        // 구분선 2
        var sep2 = MakeSeparator();

        // Footer: by 실무형 AI 추진 TF
        var lblFooter = new Label
        {
            Text      = "by  실무형 AI 추진 TF",
            Font      = new Font("Segoe UI", 9F, FontStyle.Italic),
            ForeColor = CSub,
            AutoSize  = false,
            Dock      = DockStyle.Top,
            Height    = 36,
            TextAlign = ContentAlignment.MiddleRight,
        };

        // 닫기 버튼
        var btnClose = new Button
        {
            Text        = "닫기",
            DialogResult = DialogResult.OK,
            Size        = new Size(90, 32),
            FlatStyle   = FlatStyle.Flat,
            BackColor   = CAccent,
            ForeColor   = Color.White,
            Font        = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            Cursor      = Cursors.Hand,
        };
        btnClose.FlatAppearance.BorderSize  = 0;
        btnClose.FlatAppearance.MouseOverBackColor  = CPrimary;
        btnClose.FlatAppearance.MouseDownBackColor  = Color.FromArgb(10, 55, 130);

        var pnlBtn = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 54,
            BackColor = CSurface,
        };
        pnlBtn.Controls.Add(btnClose);
        pnlBtn.Layout += (s, e) =>
        {
            var p = (Panel)s!;
            btnClose.Location = new Point(p.Width - btnClose.Width - 4, (p.Height - btnClose.Height) / 2);
        };

        // ── 본문에 컨트롤 추가 (Dock=Top 이므로 역순으로) ──────
        pnlBody.Controls.AddRange([
            pnlBtn, lblFooter, sep2,
            pnlKeys, lblShortcutTitle, sep1, lblDesc]);

        // ── 폼에 패널 추가 ────────────────────────────────────
        Controls.AddRange([pnlBody, pnlHeader]);

        AcceptButton = btnClose;
    }

    // ── 헤더 커스텀 페인트 ─────────────────────────────────────
    private static void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g  = e.Graphics;
        var rc = ((Panel)sender!).ClientRectangle;
        g.SmoothingMode    = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // 그라디언트 배경
        using var grad = new LinearGradientBrush(rc,
            Color.FromArgb(13, 71, 161), Color.FromArgb(21, 101, 192), 90f);
        g.FillRectangle(grad, rc);

        // 아이콘 영역 — 흰 원
        var circleRc = new Rectangle(24, 20, 60, 60);
        using var circleBr = new SolidBrush(Color.FromArgb(40, Color.White));
        g.FillEllipse(circleBr, circleRc);
        using var sf = new StringFormat
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("🔍", new Font("Segoe UI Emoji", 22F), Brushes.White, circleRc, sf);

        // 앱 이름
        g.DrawString("SFM",
            new Font("Segoe UI", 22F, FontStyle.Bold),
            Brushes.White, new PointF(98, 14));

        // 부제목
        g.DrawString("Smart File Manager  v1.0",
            new Font("Segoe UI", 10F),
            new SolidBrush(Color.FromArgb(180, 210, 255)),
            new PointF(100, 62));

        // 하단 얇은 강조선
        using var accentPen = new Pen(Color.FromArgb(100, Color.White), 1);
        g.DrawLine(accentPen, 0, rc.Bottom - 1, rc.Width, rc.Bottom - 1);
    }

    // ── 가로 구분선 ────────────────────────────────────────────
    private static Panel MakeSeparator()
    {
        var p = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 20,
            BackColor = Color.Transparent,
        };
        p.Paint += (s, e) =>
        {
            var w = ((Panel)s!).Width;
            using var pen = new Pen(CBorder);
            e.Graphics.DrawLine(pen, 0, 10, w, 10);
        };
        return p;
    }
}
