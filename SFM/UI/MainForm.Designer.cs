namespace SFM.UI;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        // ── 컨트롤 생성 ──────────────────────────────────────
        menuStrip           = new MenuStrip();

        tsmiFile            = new ToolStripMenuItem();
        tsmiFileNewWindow   = new ToolStripMenuItem();
        tsmiFileSep1        = new ToolStripSeparator();
        tsmiFileExit        = new ToolStripMenuItem();

        tsmiView            = new ToolStripMenuItem();
        tsmiViewDark        = new ToolStripMenuItem();
        tsmiViewSnippet     = new ToolStripMenuItem();
        tsmiViewSep1        = new ToolStripSeparator();
        tsmiViewAutoCol     = new ToolStripMenuItem();

        tsmiSearch          = new ToolStripMenuItem();
        tsmiSearchContent   = new ToolStripMenuItem();
        tsmiSearchCase      = new ToolStripMenuItem();
        tsmiSearchWhole     = new ToolStripMenuItem();
        tsmiSearchSep1      = new ToolStripSeparator();
        tsmiSearchClear     = new ToolStripMenuItem();

        tsmiTools           = new ToolStripMenuItem();
        tsmiToolsIndex      = new ToolStripMenuItem();
        tsmiToolsSep1       = new ToolStripSeparator();
        tsmiToolsReindex    = new ToolStripMenuItem();
        tsmiToolsContent    = new ToolStripMenuItem();
        tsmiToolsSep2       = new ToolStripSeparator();
        tsmiToolsVacuum     = new ToolStripMenuItem();

        tsmiHelp            = new ToolStripMenuItem();
        tsmiHelpAbout       = new ToolStripMenuItem();

        pnlSearch           = new Panel();
        txtSearch           = new TextBox();
        pnlOptions          = new Panel();
        lblFilter           = new Label();
        cmbFilter           = new ComboBox();
        chkContent          = new CheckBox();
        chkCase             = new CheckBox();
        chkWholeWord        = new CheckBox();
        lblDbInfo           = new Label();

        pnlSnippet          = new Panel();
        pnlSnippetBar       = new Panel();
        lblSnippetIcon      = new Label();
        lblSnippetPath      = new Label();
        btnSnippetClose     = new Button();
        rtbSnippet          = new RichTextBox();

        listResults         = new ListView();
        statusStrip         = new StatusStrip();
        sslResultCount      = new ToolStripStatusLabel();
        sslIndexStatus      = new ToolStripStatusLabel();
        sslDbStats          = new ToolStripStatusLabel();
        sslLastIndexed      = new ToolStripStatusLabel();

        cmsResult           = new ContextMenuStrip(components);
        tsmiOpenFile        = new ToolStripMenuItem();
        tsmiSep1            = new ToolStripSeparator();
        tsmiOpenFolder      = new ToolStripMenuItem();
        tsmiSep2            = new ToolStripSeparator();
        tsmiCopyPath        = new ToolStripMenuItem();
        tsmiCopyName        = new ToolStripMenuItem();
        tsmiSep3            = new ToolStripSeparator();
        tsmiDeleteFromIndex = new ToolStripMenuItem();

        colFileName     = new ColumnHeader();
        colPath         = new ColumnHeader();
        colSize         = new ColumnHeader();
        colModified     = new ColumnHeader();
        colMatch        = new ColumnHeader();
        imgListSmall    = new ImageList(components);

        // ── SuspendLayout ─────────────────────────────────────
        menuStrip.SuspendLayout();
        pnlSearch.SuspendLayout();
        pnlOptions.SuspendLayout();
        pnlSnippet.SuspendLayout();
        pnlSnippetBar.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();

        // ═════════════════════════════════════════════════════
        // MenuStrip
        // ═════════════════════════════════════════════════════
        menuStrip.Items.AddRange([tsmiFile, tsmiView, tsmiSearch, tsmiTools, tsmiHelp]);
        menuStrip.Location  = new Point(0, 0);
        menuStrip.Name      = "menuStrip";
        menuStrip.Size      = new Size(1024, 24);
        menuStrip.TabIndex  = 0;
        menuStrip.Text      = "menuStrip";

        // ── 파일 메뉴 ─────────────────────────────────────────
        tsmiFile.DropDownItems.AddRange([tsmiFileNewWindow, tsmiFileSep1, tsmiFileExit]);
        tsmiFile.Name = "tsmiFile";
        tsmiFile.Text = "파일(&F)";

        tsmiFileNewWindow.Name         = "tsmiFileNewWindow";
        tsmiFileNewWindow.Text         = "새 창(&N)";
        tsmiFileNewWindow.ShortcutKeys = Keys.Control | Keys.N;

        tsmiFileSep1.Name = "tsmiFileSep1";

        tsmiFileExit.Name         = "tsmiFileExit";
        tsmiFileExit.Text         = "종료(&X)";
        tsmiFileExit.ShortcutKeys = Keys.Alt | Keys.F4;

        // ── 보기 메뉴 ─────────────────────────────────────────
        tsmiView.DropDownItems.AddRange([tsmiViewDark, tsmiViewSnippet, tsmiViewSep1, tsmiViewAutoCol]);
        tsmiView.Name = "tsmiView";
        tsmiView.Text = "보기(&V)";

        tsmiViewDark.Name           = "tsmiViewDark";
        tsmiViewDark.Text           = "다크 모드(&D)";
        tsmiViewDark.CheckOnClick   = true;
        tsmiViewDark.ShortcutKeys   = Keys.Control | Keys.D;

        tsmiViewSnippet.Name         = "tsmiViewSnippet";
        tsmiViewSnippet.Text         = "미리보기 패널(&P)";
        tsmiViewSnippet.CheckOnClick = true;
        tsmiViewSnippet.Checked      = true;
        tsmiViewSnippet.ShortcutKeys = Keys.Control | Keys.P;

        tsmiViewSep1.Name = "tsmiViewSep1";

        tsmiViewAutoCol.Name         = "tsmiViewAutoCol";
        tsmiViewAutoCol.Text         = "열 너비 자동 조정(&A)";
        tsmiViewAutoCol.ShortcutKeys = Keys.Control | Keys.Add;

        // ── 검색 메뉴 ─────────────────────────────────────────
        tsmiSearch.DropDownItems.AddRange([
            tsmiSearchContent, tsmiSearchCase, tsmiSearchWhole,
            tsmiSearchSep1, tsmiSearchClear]);
        tsmiSearch.Name = "tsmiSearch";
        tsmiSearch.Text = "검색(&S)";

        tsmiSearchContent.Name         = "tsmiSearchContent";
        tsmiSearchContent.Text         = "파일 내용 검색(&C)";
        tsmiSearchContent.CheckOnClick = true;
        tsmiSearchContent.ShortcutKeys = Keys.F4;

        tsmiSearchCase.Name         = "tsmiSearchCase";
        tsmiSearchCase.Text         = "대소문자 구분(&K)";
        tsmiSearchCase.CheckOnClick = true;

        tsmiSearchWhole.Name         = "tsmiSearchWhole";
        tsmiSearchWhole.Text         = "전체 단어(&W)";
        tsmiSearchWhole.CheckOnClick = true;

        tsmiSearchSep1.Name = "tsmiSearchSep1";

        tsmiSearchClear.Name         = "tsmiSearchClear";
        tsmiSearchClear.Text         = "검색어 지우기(&L)";
        tsmiSearchClear.ShortcutKeys = Keys.Escape;

        // ── 도구 메뉴 ─────────────────────────────────────────
        tsmiTools.DropDownItems.AddRange([
            tsmiToolsIndex, tsmiToolsSep1,
            tsmiToolsReindex, tsmiToolsContent,
            tsmiToolsSep2, tsmiToolsVacuum]);
        tsmiTools.Name = "tsmiTools";
        tsmiTools.Text = "도구(&T)";

        tsmiToolsIndex.Name         = "tsmiToolsIndex";
        tsmiToolsIndex.Text         = "인덱스 설정(&I)…";
        tsmiToolsIndex.ShortcutKeys = Keys.Control | Keys.Shift | Keys.I;

        tsmiToolsSep1.Name = "tsmiToolsSep1";

        tsmiToolsReindex.Name         = "tsmiToolsReindex";
        tsmiToolsReindex.Text         = "전체 재인덱싱(&R)";
        tsmiToolsReindex.ShortcutKeys = Keys.F5;

        tsmiToolsContent.Name         = "tsmiToolsContent";
        tsmiToolsContent.Text         = "내용 인덱싱 보완(&M)";
        tsmiToolsContent.ShortcutKeys = Keys.Shift | Keys.F5;

        tsmiToolsSep2.Name = "tsmiToolsSep2";

        tsmiToolsVacuum.Name = "tsmiToolsVacuum";
        tsmiToolsVacuum.Text = "데이터베이스 최적화(&O)";

        // ── 도움말 메뉴 ──────────────────────────────────────
        tsmiHelp.DropDownItems.AddRange([tsmiHelpAbout]);
        tsmiHelp.Name = "tsmiHelp";
        tsmiHelp.Text = "도움말(&H)";

        tsmiHelpAbout.Name = "tsmiHelpAbout";
        tsmiHelpAbout.Text = "SFM 정보(&A)…";

        // ═════════════════════════════════════════════════════
        // pnlOptions (검색 옵션 행)
        // ═════════════════════════════════════════════════════
        lblFilter.AutoSize  = true;
        lblFilter.Location  = new Point(4, 7);
        lblFilter.Name      = "lblFilter";
        lblFilter.Text      = "유형:";

        cmbFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbFilter.Items.AddRange(["전체", "문서", "PDF", "이미지", "소스코드"]);
        cmbFilter.Location      = new Point(38, 3);
        cmbFilter.Name          = "cmbFilter";
        cmbFilter.SelectedIndex = 0;
        cmbFilter.Size          = new Size(90, 23);
        cmbFilter.TabIndex      = 2;
        cmbFilter.FlatStyle     = FlatStyle.Popup;

        chkContent.AutoSize  = true;
        chkContent.Location  = new Point(140, 5);
        chkContent.Name      = "chkContent";
        chkContent.Text      = "내용 검색 (F4)";
        chkContent.TabIndex  = 3;

        chkCase.AutoSize  = true;
        chkCase.Location  = new Point(252, 5);
        chkCase.Name      = "chkCase";
        chkCase.Text      = "대소문자";
        chkCase.TabIndex  = 4;

        chkWholeWord.AutoSize  = true;
        chkWholeWord.Location  = new Point(338, 5);
        chkWholeWord.Name      = "chkWholeWord";
        chkWholeWord.Text      = "전체 단어";
        chkWholeWord.TabIndex  = 5;

        lblDbInfo.AutoSize   = false;
        lblDbInfo.Anchor     = AnchorStyles.Top | AnchorStyles.Right;
        lblDbInfo.TextAlign  = ContentAlignment.MiddleRight;
        lblDbInfo.Location   = new Point(700, 4);
        lblDbInfo.Size       = new Size(310, 20);
        lblDbInfo.Name       = "lblDbInfo";
        lblDbInfo.Text       = string.Empty;
        lblDbInfo.ForeColor  = SystemColors.GrayText;

        pnlOptions.Controls.AddRange([lblFilter, cmbFilter, chkContent, chkCase, chkWholeWord, lblDbInfo]);
        pnlOptions.Dock      = DockStyle.Bottom;
        pnlOptions.Height    = 30;
        pnlOptions.Name      = "pnlOptions";
        pnlOptions.Padding   = new Padding(4, 0, 4, 0);

        // ═════════════════════════════════════════════════════
        // txtSearch
        // ═════════════════════════════════════════════════════
        txtSearch.Anchor          = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtSearch.Font            = new Font("Segoe UI", 11F);
        txtSearch.Location        = new Point(8, 8);
        txtSearch.Name            = "txtSearch";
        txtSearch.PlaceholderText = "파일명 또는 내용을 검색하세요…  (Ctrl+F)";
        txtSearch.Size            = new Size(1000, 28);
        txtSearch.TabIndex        = 1;
        txtSearch.BorderStyle     = BorderStyle.FixedSingle;

        // ═════════════════════════════════════════════════════
        // pnlSearch
        // ═════════════════════════════════════════════════════
        pnlSearch.Controls.AddRange([txtSearch, pnlOptions]);
        pnlSearch.Dock    = DockStyle.Top;
        pnlSearch.Height  = 72;
        pnlSearch.Name    = "pnlSearch";
        pnlSearch.Padding = new Padding(4, 0, 8, 0);

        // ═════════════════════════════════════════════════════
        // Snippet 패널
        // ═════════════════════════════════════════════════════
        lblSnippetIcon.AutoSize  = false;
        lblSnippetIcon.Location  = new Point(4, 3);
        lblSnippetIcon.Size      = new Size(20, 18);
        lblSnippetIcon.Text      = "📄";
        lblSnippetIcon.Name      = "lblSnippetIcon";

        lblSnippetPath.AutoSize  = false;
        lblSnippetPath.Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblSnippetPath.Location  = new Point(26, 4);
        lblSnippetPath.Name      = "lblSnippetPath";
        lblSnippetPath.Size      = new Size(960, 16);
        lblSnippetPath.Text      = "항목을 선택하면 미리보기가 표시됩니다";
        lblSnippetPath.ForeColor = SystemColors.GrayText;

        btnSnippetClose.Anchor    = AnchorStyles.Top | AnchorStyles.Right;
        btnSnippetClose.FlatStyle = FlatStyle.Flat;
        btnSnippetClose.FlatAppearance.BorderSize = 0;
        btnSnippetClose.Font      = new Font("Segoe UI", 8F);
        btnSnippetClose.Location  = new Point(990, 2);
        btnSnippetClose.Name      = "btnSnippetClose";
        btnSnippetClose.Size      = new Size(22, 20);
        btnSnippetClose.Text      = "✕";
        btnSnippetClose.TabStop   = false;

        pnlSnippetBar.Controls.AddRange([lblSnippetIcon, lblSnippetPath, btnSnippetClose]);
        pnlSnippetBar.Dock      = DockStyle.Top;
        pnlSnippetBar.Height    = 24;
        pnlSnippetBar.Name      = "pnlSnippetBar";
        pnlSnippetBar.Padding   = new Padding(2, 0, 2, 0);

        rtbSnippet.Dock           = DockStyle.Fill;
        rtbSnippet.BorderStyle    = BorderStyle.None;
        rtbSnippet.Font           = new Font("Consolas", 9F);
        rtbSnippet.Name           = "rtbSnippet";
        rtbSnippet.ReadOnly       = true;
        rtbSnippet.ScrollBars     = RichTextBoxScrollBars.Vertical;
        rtbSnippet.TabStop        = false;
        rtbSnippet.WordWrap       = true;
        rtbSnippet.DetectUrls     = false;

        pnlSnippet.Controls.AddRange([rtbSnippet, pnlSnippetBar]);
        pnlSnippet.Dock    = DockStyle.Bottom;
        pnlSnippet.Height  = 110;
        pnlSnippet.Name    = "pnlSnippet";

        // ═════════════════════════════════════════════════════
        // ListView 컬럼
        // ═════════════════════════════════════════════════════
        colFileName.Text  = "파일명";
        colFileName.Width = 240;

        colPath.Text  = "경로";
        colPath.Width = 340;

        colSize.Text      = "크기";
        colSize.TextAlign = HorizontalAlignment.Right;
        colSize.Width     = 80;

        colModified.Text  = "수정일";
        colModified.Width = 145;

        colMatch.Text  = "일치";
        colMatch.Width = 66;

        // ═════════════════════════════════════════════════════
        // listResults
        // ═════════════════════════════════════════════════════
        listResults.Columns.AddRange([colFileName, colPath, colSize, colModified, colMatch]);
        listResults.ContextMenuStrip                = cmsResult;
        listResults.Dock                            = DockStyle.Fill;
        listResults.Font                            = new Font("Segoe UI", 9.5F);
        listResults.FullRowSelect                   = true;
        listResults.GridLines                       = false;
        listResults.HideSelection                   = false;
        listResults.MultiSelect                     = true;
        listResults.Name                            = "listResults";
        listResults.ShowItemToolTips                = true;
        listResults.SmallImageList                  = imgListSmall;
        listResults.TabIndex                        = 0;
        listResults.UseCompatibleStateImageBehavior = false;
        listResults.View                            = View.Details;
        listResults.VirtualMode                     = true;

        // ═════════════════════════════════════════════════════
        // StatusStrip
        // ═════════════════════════════════════════════════════
        sslResultCount.AutoSize = true;
        sslResultCount.Name     = "sslResultCount";
        sslResultCount.Text     = "준비";
        sslResultCount.Padding  = new Padding(4, 0, 8, 0);

        sslIndexStatus.AutoSize  = true;
        sslIndexStatus.Name      = "sslIndexStatus";
        sslIndexStatus.Spring    = true;
        sslIndexStatus.Text      = string.Empty;
        sslIndexStatus.TextAlign = ContentAlignment.MiddleCenter;

        sslDbStats.AutoSize = true;
        sslDbStats.Name     = "sslDbStats";
        sslDbStats.Text     = string.Empty;
        sslDbStats.Padding  = new Padding(8, 0, 8, 0);

        sslLastIndexed.AutoSize = true;
        sslLastIndexed.Name     = "sslLastIndexed";
        sslLastIndexed.Text     = string.Empty;
        sslLastIndexed.Padding  = new Padding(0, 0, 4, 0);

        statusStrip.Items.AddRange([sslResultCount, sslIndexStatus, sslDbStats, sslLastIndexed]);
        statusStrip.Name       = "statusStrip";
        statusStrip.SizingGrip = true;
        statusStrip.TabIndex   = 6;

        // ═════════════════════════════════════════════════════
        // ContextMenuStrip
        // ═════════════════════════════════════════════════════
        tsmiOpenFile.Name    = "tsmiOpenFile";
        tsmiOpenFile.Text    = "파일 열기(&O)";
        tsmiOpenFile.Font    = new Font("Segoe UI", 9F, FontStyle.Bold);
        tsmiOpenFile.ShortcutKeyDisplayString = "Enter";

        tsmiSep1.Name = "tsmiSep1";

        tsmiOpenFolder.Name = "tsmiOpenFolder";
        tsmiOpenFolder.Text = "파일 위치 열기(&E)";

        tsmiSep2.Name = "tsmiSep2";

        tsmiCopyPath.Name         = "tsmiCopyPath";
        tsmiCopyPath.Text         = "전체 경로 복사(&C)";
        tsmiCopyPath.ShortcutKeyDisplayString = "Ctrl+C";

        tsmiCopyName.Name = "tsmiCopyName";
        tsmiCopyName.Text = "파일명 복사(&N)";

        tsmiSep3.Name = "tsmiSep3";

        tsmiDeleteFromIndex.Name      = "tsmiDeleteFromIndex";
        tsmiDeleteFromIndex.Text      = "인덱스에서 제거(&D)";
        tsmiDeleteFromIndex.ForeColor = Color.FromArgb(180, 0, 0);

        cmsResult.Items.AddRange([
            tsmiOpenFile, tsmiSep1,
            tsmiOpenFolder, tsmiSep2,
            tsmiCopyPath, tsmiCopyName, tsmiSep3,
            tsmiDeleteFromIndex]);
        cmsResult.Name = "cmsResult";

        // ═════════════════════════════════════════════════════
        // ImageList
        // ═════════════════════════════════════════════════════
        imgListSmall.ColorDepth       = ColorDepth.Depth32Bit;
        imgListSmall.ImageSize        = new Size(16, 16);
        imgListSmall.TransparentColor = Color.Transparent;

        // ═════════════════════════════════════════════════════
        // MainForm
        // ═════════════════════════════════════════════════════
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        ClientSize          = new Size(1024, 680);
        Font                = new Font("Segoe UI", 9F);
        MainMenuStrip       = menuStrip;
        MinimumSize         = new Size(720, 480);
        Name                = "MainForm";
        StartPosition       = FormStartPosition.CenterScreen;
        Text                = "SFM — Smart File Manager";

        // 도킹 순서: Fill → Top → Bottom 순으로 추가
        Controls.Add(listResults);
        Controls.Add(pnlSnippet);
        Controls.Add(pnlSearch);
        Controls.Add(menuStrip);
        Controls.Add(statusStrip);

        menuStrip.ResumeLayout(false);
        menuStrip.PerformLayout();
        pnlSearch.ResumeLayout(false);
        pnlOptions.ResumeLayout(false);
        pnlSnippet.ResumeLayout(false);
        pnlSnippetBar.ResumeLayout(false);
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    // ── 컨트롤 선언 ──────────────────────────────────────────
    private MenuStrip            menuStrip           = null!;
    private ToolStripMenuItem    tsmiFile            = null!;
    private ToolStripMenuItem    tsmiFileNewWindow   = null!;
    private ToolStripSeparator   tsmiFileSep1        = null!;
    private ToolStripMenuItem    tsmiFileExit        = null!;
    private ToolStripMenuItem    tsmiView            = null!;
    private ToolStripMenuItem    tsmiViewDark        = null!;
    private ToolStripMenuItem    tsmiViewSnippet     = null!;
    private ToolStripSeparator   tsmiViewSep1        = null!;
    private ToolStripMenuItem    tsmiViewAutoCol     = null!;
    private ToolStripMenuItem    tsmiSearch          = null!;
    private ToolStripMenuItem    tsmiSearchContent   = null!;
    private ToolStripMenuItem    tsmiSearchCase      = null!;
    private ToolStripMenuItem    tsmiSearchWhole     = null!;
    private ToolStripSeparator   tsmiSearchSep1      = null!;
    private ToolStripMenuItem    tsmiSearchClear     = null!;
    private ToolStripMenuItem    tsmiTools           = null!;
    private ToolStripMenuItem    tsmiToolsIndex      = null!;
    private ToolStripSeparator   tsmiToolsSep1       = null!;
    private ToolStripMenuItem    tsmiToolsReindex    = null!;
    private ToolStripMenuItem    tsmiToolsContent    = null!;
    private ToolStripSeparator   tsmiToolsSep2       = null!;
    private ToolStripMenuItem    tsmiToolsVacuum     = null!;
    private ToolStripMenuItem    tsmiHelp            = null!;
    private ToolStripMenuItem    tsmiHelpAbout       = null!;

    private Panel                pnlSearch           = null!;
    private TextBox              txtSearch           = null!;
    private Panel                pnlOptions          = null!;
    private Label                lblFilter           = null!;
    private ComboBox             cmbFilter           = null!;
    private CheckBox             chkContent          = null!;
    private CheckBox             chkCase             = null!;
    private CheckBox             chkWholeWord        = null!;
    private Label                lblDbInfo           = null!;

    private Panel                pnlSnippet          = null!;
    private Panel                pnlSnippetBar       = null!;
    private Label                lblSnippetIcon      = null!;
    private Label                lblSnippetPath      = null!;
    private Button               btnSnippetClose     = null!;
    private RichTextBox          rtbSnippet          = null!;

    private ListView             listResults         = null!;
    private StatusStrip          statusStrip         = null!;
    private ToolStripStatusLabel sslResultCount      = null!;
    private ToolStripStatusLabel sslIndexStatus      = null!;
    private ToolStripStatusLabel sslDbStats          = null!;
    private ToolStripStatusLabel sslLastIndexed      = null!;

    private ContextMenuStrip     cmsResult           = null!;
    private ToolStripMenuItem    tsmiOpenFile        = null!;
    private ToolStripSeparator   tsmiSep1            = null!;
    private ToolStripMenuItem    tsmiOpenFolder      = null!;
    private ToolStripSeparator   tsmiSep2            = null!;
    private ToolStripMenuItem    tsmiCopyPath        = null!;
    private ToolStripMenuItem    tsmiCopyName        = null!;
    private ToolStripSeparator   tsmiSep3            = null!;
    private ToolStripMenuItem    tsmiDeleteFromIndex = null!;

    private ColumnHeader         colFileName         = null!;
    private ColumnHeader         colPath             = null!;
    private ColumnHeader         colSize             = null!;
    private ColumnHeader         colModified         = null!;
    private ColumnHeader         colMatch            = null!;
    private ImageList            imgListSmall        = null!;
}
