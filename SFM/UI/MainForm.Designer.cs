namespace SFM.UI;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        // ── 컨트롤 생성 ──────────────────────────────────────
        pnlTop          = new Panel();
        txtSearch       = new TextBox();
        btnSearch       = new Button();
        cmbFilter       = new ComboBox();
        btnSettings     = new Button();
        listResults     = new ListView();
        statusStrip     = new StatusStrip();
        sslResultCount  = new ToolStripStatusLabel();
        sslIndexStatus  = new ToolStripStatusLabel();
        sslLastIndexed  = new ToolStripStatusLabel();
        cmsResult       = new ContextMenuStrip(components);
        tsmiOpenFile    = new ToolStripMenuItem();
        tsmiSep1        = new ToolStripSeparator();
        tsmiOpenFolder  = new ToolStripMenuItem();
        tsmiCopyPath    = new ToolStripMenuItem();
        colFileName     = new ColumnHeader();
        colPath         = new ColumnHeader();
        colSize         = new ColumnHeader();
        colModified     = new ColumnHeader();
        colMatch        = new ColumnHeader();
        imgListSmall    = new ImageList(components);

        pnlTop.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();

        // ─────────────────────────────────────────────────────
        // txtSearch
        // ─────────────────────────────────────────────────────
        txtSearch.Anchor          = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtSearch.Font            = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
        txtSearch.Location        = new Point(6, 9);
        txtSearch.Name            = "txtSearch";
        txtSearch.PlaceholderText = "파일명 또는 내용을 검색하세요…";
        txtSearch.Size            = new Size(796, 27);
        txtSearch.TabIndex        = 0;

        // ─────────────────────────────────────────────────────
        // btnSearch
        // ─────────────────────────────────────────────────────
        btnSearch.Anchor                  = AnchorStyles.Top | AnchorStyles.Right;
        btnSearch.Location                = new Point(806, 9);
        btnSearch.Name                    = "btnSearch";
        btnSearch.Size                    = new Size(60, 27);
        btnSearch.TabIndex                = 1;
        btnSearch.Text                    = "검색";
        btnSearch.UseVisualStyleBackColor = true;

        // ─────────────────────────────────────────────────────
        // cmbFilter
        // ─────────────────────────────────────────────────────
        cmbFilter.Anchor        = AnchorStyles.Top | AnchorStyles.Right;
        cmbFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbFilter.Items.AddRange(["전체", "문서", "PDF", "이미지", "소스코드"]);
        cmbFilter.Location      = new Point(870, 10);
        cmbFilter.Name          = "cmbFilter";
        cmbFilter.SelectedIndex = 0;
        cmbFilter.Size          = new Size(110, 25);
        cmbFilter.TabIndex      = 2;

        // ─────────────────────────────────────────────────────
        // btnSettings
        // ─────────────────────────────────────────────────────
        btnSettings.Anchor                  = AnchorStyles.Top | AnchorStyles.Right;
        btnSettings.Font                    = new Font("Segoe UI", 13F, FontStyle.Regular, GraphicsUnit.Point);
        btnSettings.Location                = new Point(984, 9);
        btnSettings.Name                    = "btnSettings";
        btnSettings.Size                    = new Size(34, 27);
        btnSettings.TabIndex                = 3;
        btnSettings.Text                    = "⚙";
        btnSettings.UseVisualStyleBackColor = true;

        // ─────────────────────────────────────────────────────
        // pnlTop
        // ─────────────────────────────────────────────────────
        pnlTop.Controls.AddRange([txtSearch, btnSearch, cmbFilter, btnSettings]);
        pnlTop.Dock    = DockStyle.Top;
        pnlTop.Height  = 46;
        pnlTop.Name    = "pnlTop";
        pnlTop.TabIndex = 0;

        // ─────────────────────────────────────────────────────
        // ListView 컬럼
        // ─────────────────────────────────────────────────────
        colFileName.Text  = "파일명";
        colFileName.Width = 220;

        colPath.Text  = "경로";
        colPath.Width = 300;

        colSize.Text      = "크기";
        colSize.TextAlign = HorizontalAlignment.Right;
        colSize.Width     = 75;

        colModified.Text  = "수정일";
        colModified.Width = 135;

        colMatch.Text  = "일치";
        colMatch.Width = 70;

        // ─────────────────────────────────────────────────────
        // listResults
        // ─────────────────────────────────────────────────────
        listResults.Columns.AddRange([colFileName, colPath, colSize, colModified, colMatch]);
        listResults.ContextMenuStrip                  = cmsResult;
        listResults.Dock                              = DockStyle.Fill;
        listResults.FullRowSelect                     = true;
        listResults.GridLines                         = false;
        listResults.HideSelection                     = false;
        listResults.MultiSelect                       = false;
        listResults.Name                              = "listResults";
        listResults.ShowItemToolTips                  = true;
        listResults.SmallImageList                    = imgListSmall;
        listResults.TabIndex                          = 4;
        listResults.UseCompatibleStateImageBehavior   = false;
        listResults.View                              = View.Details;
        listResults.VirtualMode                       = true;

        // ─────────────────────────────────────────────────────
        // StatusStrip
        // ─────────────────────────────────────────────────────
        sslResultCount.AutoSize = true;
        sslResultCount.Name     = "sslResultCount";
        sslResultCount.Text     = "준비";

        sslIndexStatus.AutoSize   = true;
        sslIndexStatus.Name       = "sslIndexStatus";
        sslIndexStatus.Spring     = true;          // 중앙 공간 채움
        sslIndexStatus.Text       = string.Empty;
        sslIndexStatus.TextAlign  = ContentAlignment.MiddleCenter;

        sslLastIndexed.AutoSize = true;
        sslLastIndexed.Name     = "sslLastIndexed";
        sslLastIndexed.Text     = string.Empty;

        statusStrip.Items.AddRange([sslResultCount, sslIndexStatus, sslLastIndexed]);
        statusStrip.Name      = "statusStrip";
        statusStrip.SizingGrip = true;
        statusStrip.TabIndex  = 5;
        statusStrip.Text      = "statusStrip";

        // ─────────────────────────────────────────────────────
        // ContextMenuStrip
        // ─────────────────────────────────────────────────────
        tsmiOpenFile.Name    = "tsmiOpenFile";
        tsmiOpenFile.Text    = "파일 열기";

        tsmiSep1.Name = "tsmiSep1";

        tsmiOpenFolder.Name = "tsmiOpenFolder";
        tsmiOpenFolder.Text = "파일 위치 열기";

        tsmiCopyPath.Name = "tsmiCopyPath";
        tsmiCopyPath.Text = "경로 복사";

        cmsResult.Items.AddRange([tsmiOpenFile, tsmiSep1, tsmiOpenFolder, tsmiCopyPath]);
        cmsResult.Name = "cmsResult";

        // ─────────────────────────────────────────────────────
        // ImageList
        // ─────────────────────────────────────────────────────
        imgListSmall.ColorDepth       = ColorDepth.Depth32Bit;
        imgListSmall.ImageSize        = new Size(16, 16);
        imgListSmall.TransparentColor = Color.Transparent;

        // ─────────────────────────────────────────────────────
        // MainForm
        // ─────────────────────────────────────────────────────
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        ClientSize          = new Size(1024, 680);
        Font                = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        MinimumSize         = new Size(800, 500);
        Name                = "MainForm";
        StartPosition       = FormStartPosition.CenterScreen;
        Text                = "SFM — Smart File Manager";

        // 추가 순서: Fill(뒤) → Top(중간) → Bottom(앞) — 도킹 우선순위 결정
        Controls.Add(listResults);
        Controls.Add(pnlTop);
        Controls.Add(statusStrip);

        pnlTop.ResumeLayout(false);
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    // ── 컨트롤 선언 ──────────────────────────────────────────
    private Panel               pnlTop         = null!;
    private TextBox             txtSearch      = null!;
    private Button              btnSearch      = null!;
    private ComboBox            cmbFilter      = null!;
    private Button              btnSettings    = null!;
    private ListView            listResults    = null!;
    private StatusStrip         statusStrip    = null!;
    private ToolStripStatusLabel sslResultCount = null!;
    private ToolStripStatusLabel sslIndexStatus = null!;
    private ToolStripStatusLabel sslLastIndexed = null!;
    private ContextMenuStrip    cmsResult      = null!;
    private ToolStripMenuItem   tsmiOpenFile   = null!;
    private ToolStripSeparator  tsmiSep1       = null!;
    private ToolStripMenuItem   tsmiOpenFolder = null!;
    private ToolStripMenuItem   tsmiCopyPath   = null!;
    private ColumnHeader        colFileName    = null!;
    private ColumnHeader        colPath        = null!;
    private ColumnHeader        colSize        = null!;
    private ColumnHeader        colModified    = null!;
    private ColumnHeader        colMatch       = null!;
    private ImageList           imgListSmall   = null!;
}
