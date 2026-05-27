using System.Drawing.Drawing2D;

namespace SFM.UI;

/// <summary>
/// 앱 시작 시 즉시 표시되는 스플래시 화면.
/// Label 대신 Paint 이벤트로 직접 그려서 글자 가려짐·투명도 문제를 방지한다.
/// </summary>
public sealed class SplashForm : Form
{
    private static readonly Color CPrimary = Color.FromArgb(13,  71, 161);
    private static readonly Color CLighter = Color.FromArgb(21, 101, 192);

    private readonly System.Windows.Forms.Timer _dotTimer;
    private int _dotCount = 0;

    public SplashForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        Size            = new Size(420, 220);
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = CPrimary;
        TopMost         = true;
        ShowInTaskbar   = false;
        DoubleBuffered  = true;   // 깜빡임 방지

        _dotTimer       = new System.Windows.Forms.Timer { Interval = 400 };
        _dotTimer.Tick += (_, _) =>
        {
            _dotCount = (_dotCount + 1) % 4;
            Invalidate();   // Paint 재호출로 점 애니메이션
        };

        Paint += OnPaint;
        Shown += (_, _) => _dotTimer.Start();
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        var g  = e.Graphics;
        var rc = ClientRectangle;
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // ── 배경 그라디언트 ───────────────────────────────────
        using var grad = new LinearGradientBrush(rc, CPrimary, CLighter, 135f);
        g.FillRectangle(grad, rc);

        // ── 장식 원 (우하단) ──────────────────────────────────
        using var circleBr = new SolidBrush(Color.FromArgb(18, Color.White));
        g.FillEllipse(circleBr, rc.Width - 120, rc.Height - 90, 200, 200);
        // 장식 원 (좌상단)
        g.FillEllipse(circleBr, -80, -80, 200, 200);

        // ── 하단 강조선 ───────────────────────────────────────
        using var accentPen = new Pen(Color.FromArgb(50, Color.White));
        g.DrawLine(accentPen, 0, rc.Bottom - 1, rc.Width, rc.Bottom - 1);

        // ── 텍스트 공통 ───────────────────────────────────────
        using var sfCenter = new StringFormat
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        // "SFM" 타이틀
        using var fntTitle = new Font("Segoe UI", 40F, FontStyle.Bold, GraphicsUnit.Point);
        using var brWhite  = new SolidBrush(Color.White);
        g.DrawString("SFM", fntTitle, brWhite,
            new RectangleF(0, 20, rc.Width, 70), sfCenter);

        // 부제목
        using var fntSub = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point);
        using var brSub  = new SolidBrush(Color.FromArgb(190, 215, 255));
        g.DrawString("Smart File Manager", fntSub, brSub,
            new RectangleF(0, 95, rc.Width, 30), sfCenter);

        // 버전
        using var fntVer = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        using var brVer  = new SolidBrush(Color.FromArgb(140, 170, 220));
        g.DrawString("v1.0", fntVer, brVer,
            new RectangleF(0, 126, rc.Width, 20), sfCenter);

        // "시작 중..."  점 애니메이션
        using var fntStatus = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        using var brStatus  = new SolidBrush(Color.FromArgb(210, 225, 255));
        var dots = new string('.', _dotCount);
        g.DrawString("시작 중" + dots, fntStatus, brStatus,
            new RectangleF(0, 163, rc.Width, 30), sfCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _dotTimer.Dispose();
        base.Dispose(disposing);
    }
}
