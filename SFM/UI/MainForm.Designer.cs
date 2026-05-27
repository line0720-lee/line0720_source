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

        // ── 메뉴 ─────────────────────────────────────────────────
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
        tsmiSearchSep1      = new ToolStripSeparator();
        tsmiSearchClear     = new ToolStripMenuItem();
        tsmiTools           = new ToolStripMenuItem();
        tsmiToolsReindex    = new ToolStripMenuItem();
        tsmiToolsSep2       = new ToolStripSeparator();
        tsmiToolsVacuum     = new ToolStripMenuItem();
        tsmiToolsSep3       = new ToolStripSeparator();
        tsmiToolsResetIndex = new ToolStripMenuItem();
        tsmiHelp            = new ToolStripMenuItem();
        tsmiHelpAbout       = new ToolStripMenuItem();

        // ── 툴바 패널 ─────────────────────────────────────────────
        pnlSearch           = new Panel();
        tblToolbar          = new TableLayoutPanel();
        txtSearch           = new TextBox();
        btnSearchHint       = new Button();
        btnClear            = new Button();
        btnSearch           = new Button();
        lblAiNotice         = new Label();

        // ── 옵션 패널 ─────────────────────────────────────────────
        pnlOptions          = new Panel();
        chkContent          = new CheckBox();
        lblTypeSep          = new Label();
        lblFilter           = new Label();
        chkTypeDoc          = new CheckBox();
        chkTypePdf          = new CheckBox();
        chkTypeImage        = new CheckBox();
        chkTypeCode         = new CheckBox();
        lblDbInfo           = new Label();

        // ── 스니펫 패널 ──────────────────────────────────────────
        pnlSnippet          = new Panel();
        pnlSnippetBar       = new Panel();
        lblSnippetIcon      = new Label();
        lblSnippetPath      = new Label();
        btnSnippetClose     = new Button();
        rtbSnippet          = new RichTextBox();

        // ── 결과 목록 ────────────────────────────────────────────
        listResults         = new ExtListView();

        // ── 좌우 분할 컨테이너 (결과 목록 | 미리보기) ────────────
        splitPreview        = new SplitContainer();

        // ── 상태바 ───────────────────────────────────────────────
        statusStrip         = new StatusStrip();
        sslResultCount      = new ToolStripStatusLabel();
        sslIndexStatus      = new ToolStripStatusLabel();
        sslDbStats          = new ToolStripStatusLabel();
        sslLastIndexed      = new ToolStripStatusLabel();

        // ── 우클릭 메뉴 ───────────────────────────────────────────
        cmsResult           = new ContextMenuStrip(components);
        tsmiOpenFile        = new ToolStripMenuItem();
        tsmiOpenFolder      = new ToolStripMenuItem();
        tsmiSep1            = new ToolStripSeparator();
        tsmiDeleteFile      = new ToolStripMenuItem();
        tsmiProperties      = new ToolStripMenuItem();
        tsmiSep2            = new ToolStripSeparator();
        tsmiSelectAll       = new ToolStripMenuItem();
        tsmiSep3            = new ToolStripSeparator();
        tsmiCopyPath        = new ToolStripMenuItem();
        tsmiCopyDir         = new ToolStripMenuItem();
        tsmiCopyName        = new ToolStripMenuItem();
        tsmiSep4            = new ToolStripSeparator();
        tsmiDeleteFromIndex = new ToolStripMenuItem();

        // ── 첫 실행 오버레이 ──────────────────────────────────────
        pnlOverlay          = new Panel();
        pnlOverlayCard      = new Panel();
        lblOverlayIcon      = new Label();
        lblOverlayTitle     = new Label();
        lblOverlayDesc      = new Label();
        pbOverlay           = new ProgressBar();
        lblOverlayProgress  = new Label();
        lblOverlayNote      = new Label();
        btnOverlayClose     = new Button();

        // ── ListView 컬럼 ─────────────────────────────────────────
        colFileName         = new ColumnHeader();
        colPath             = new ColumnHeader();
        colSize             = new ColumnHeader();
        colModified         = new ColumnHeader();
        colMatch            = new ColumnHeader();
        imgListSmall        = new ImageList(components);

        // ─────────────────────────────────────────────────────────
        // SuspendLayout
        // ─────────────────────────────────────────────────────────
        splitPreview.Panel1.SuspendLayout();
        splitPreview.Panel2.SuspendLayout();
        splitPreview.SuspendLayout();

        menuStrip.SuspendLayout();
        pnlSearch.SuspendLayout();
        tblToolbar.SuspendLayout();
        pnlOptions.SuspendLayout();
        pnlSnippet.SuspendLayout();
        pnlSnippetBar.SuspendLayout();
        statusStrip.SuspendLayout();
        SuspendLayout();

        // ═════════════════════════════════════════════════════════
        // MenuStrip
        // ═════════════════════════════════════════════════════════
        menuStrip.Items.AddRange([tsmiFile, tsmiView, tsmiSearch, tsmiTools, tsmiHelp]);
        menuStrip.Location = new Point(0, 0);
        menuStrip.Name     = "menuStrip";
        menuStrip.Size     = new Size(1024, 24);
        menuStrip.TabIndex = 0;

        // 파일 메뉴
        tsmiFile.DropDownItems.AddRange([tsmiFileNewWindow, tsmiFileSep1, tsmiFileExit]);
        tsmiFile.Name = "tsmiFile"; tsmiFile.Text = "파일(&F)";
        tsmiFileNewWindow.Name = "tsmiFileNewWindow"; tsmiFileNewWindow.Text = "새 창(&N)";
        tsmiFileNewWindow.ShortcutKeys = Keys.Control | Keys.N;
        tsmiFileSep1.Name = "tsmiFileSep1";
        tsmiFileExit.Name = "tsmiFileExit"; tsmiFileExit.Text = "종료(&X)";
        tsmiFileExit.ShortcutKeys = Keys.Alt | Keys.F4;

        // 보기 메뉴
        tsmiView.DropDownItems.AddRange([tsmiViewDark, tsmiViewSnippet, tsmiViewSep1, tsmiViewAutoCol]);
        tsmiView.Name = "tsmiView"; tsmiView.Text = "보기(&V)";
        tsmiViewDark.Name = "tsmiViewDark"; tsmiViewDark.Text = "다크 모드(&D)";
        tsmiViewDark.CheckOnClick = true; tsmiViewDark.ShortcutKeys = Keys.Control | Keys.D;
        tsmiViewSnippet.Name = "tsmiViewSnippet"; tsmiViewSnippet.Text = "미리보기 패널(&P)";
        tsmiViewSnippet.CheckOnClick = true; tsmiViewSnippet.Checked = true;
        tsmiViewSnippet.ShortcutKeys = Keys.Control | Keys.P;
        tsmiViewSep1.Name = "tsmiViewSep1";
        tsmiViewAutoCol.Name = "tsmiViewAutoCol"; tsmiViewAutoCol.Text = "열 너비 자동 조정(&A)";
        tsmiViewAutoCol.CheckOnClick = true; tsmiViewAutoCol.Checked = true;
        tsmiViewAutoCol.ShortcutKeys = Keys.Control | Keys.Add;

        // 검색 메뉴
        tsmiSearch.DropDownItems.AddRange([tsmiSearchContent, tsmiSearchSep1, tsmiSearchClear]);
        tsmiSearch.Name = "tsmiSearch"; tsmiSearch.Text = "검색(&S)";
        tsmiSearchContent.Name = "tsmiSearchContent"; tsmiSearchContent.Text = "파일 내용 검색(&C)";
        tsmiSearchContent.CheckOnClick = true; tsmiSearchContent.ShortcutKeys = Keys.F4;
        tsmiSearchSep1.Name = "tsmiSearchSep1";
        tsmiSearchClear.Name = "tsmiSearchClear"; tsmiSearchClear.Text = "검색어 지우기(&L)";
        tsmiSearchClear.ShortcutKeyDisplayString = "Esc";

        // 도구 메뉴
        tsmiTools.DropDownItems.AddRange([
            tsmiToolsReindex,
            tsmiToolsSep2, tsmiToolsVacuum,
            tsmiToolsSep3, tsmiToolsResetIndex]);
        tsmiTools.Name = "tsmiTools"; tsmiTools.Text = "도구(&T)";
        tsmiToolsReindex.Name = "tsmiToolsReindex"; tsmiToolsReindex.Text = "전체 재인덱싱(&R)";
        tsmiToolsReindex.ShortcutKeys = Keys.F5;
        tsmiToolsSep2.Name = "tsmiToolsSep2";
        tsmiToolsVacuum.Name = "tsmiToolsVacuum"; tsmiToolsVacuum.Text = "데이터베이스 최적화(&O)";
        tsmiToolsSep3.Name = "tsmiToolsSep3";
        tsmiToolsResetIndex.Name = "tsmiToolsResetIndex"; tsmiToolsResetIndex.Text = "인덱스 초기화(&I)…";
        tsmiToolsResetIndex.ForeColor = Color.FromArgb(180, 0, 0);

        // 도움말 메뉴
        tsmiHelp.DropDownItems.AddRange([tsmiHelpAbout]);
        tsmiHelp.Name = "tsmiHelp"; tsmiHelp.Text = "도움말(&H)";
        tsmiHelpAbout.Name = "tsmiHelpAbout"; tsmiHelpAbout.Text = "SFM 정보(&A)…";

        // ═════════════════════════════════════════════════════════
        // 검색창 (txtSearch)
        // ═════════════════════════════════════════════════════════
        txtSearch.Anchor          = AnchorStyles.Left | AnchorStyles.Right;
        txtSearch.BorderStyle     = BorderStyle.FixedSingle;
        txtSearch.Font            = new Font("Segoe UI", 10F);
        txtSearch.Margin          = new Padding(0);
        txtSearch.Name            = "txtSearch";
        txtSearch.PlaceholderText = "검색어(2자이상)를 입력하세요...^.^";
        txtSearch.TabIndex        = 1;

        // ═════════════════════════════════════════════════════════
        // 지우기 버튼 (btnClear)
        // ═════════════════════════════════════════════════════════
        btnClear.Dock                      = DockStyle.Fill;
        btnClear.FlatStyle                 = FlatStyle.Flat;
        btnClear.FlatAppearance.BorderSize = 0;
        btnClear.Font                      = new Font("Segoe UI", 10F);
        btnClear.Margin                    = new Padding(2, 0, 2, 0);
        btnClear.Name                      = "btnClear";
        btnClear.TabStop                   = false;
        btnClear.Text                      = "✕";

        // ═════════════════════════════════════════════════════════
        // 검색 버튼 (btnSearch)
        // ═════════════════════════════════════════════════════════
        btnSearch.Dock                       = DockStyle.Fill;
        btnSearch.FlatStyle                  = FlatStyle.Flat;
        btnSearch.FlatAppearance.BorderColor = Color.FromArgb(0, 90, 180);
        btnSearch.BackColor                  = Color.FromArgb(0, 120, 212);
        btnSearch.ForeColor                  = Color.White;
        btnSearch.Font                       = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        btnSearch.Margin                     = new Padding(0);
        btnSearch.Name                       = "btnSearch";
        btnSearch.TabIndex                   = 2;
        btnSearch.Text                       = "검색";

        // ═════════════════════════════════════════════════════════
        // 검색 힌트 버튼 (?)
        // ═════════════════════════════════════════════════════════
        btnSearchHint.Dock                              = DockStyle.Fill;
        btnSearchHint.FlatStyle                         = FlatStyle.Flat;
        btnSearchHint.FlatAppearance.BorderSize         = 0;
        btnSearchHint.FlatAppearance.MouseOverBackColor = Color.FromArgb(20, 25, 118, 210);
        btnSearchHint.FlatAppearance.MouseDownBackColor = Color.FromArgb(40, 25, 118, 210);
        btnSearchHint.BackColor                         = Color.Transparent;
        btnSearchHint.ForeColor                         = Color.FromArgb(25, 118, 210);
        btnSearchHint.Font                              = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        btnSearchHint.Margin                            = new Padding(2, 4, 0, 4);
        btnSearchHint.Name                              = "btnSearchHint";
        btnSearchHint.TabStop                           = false;
        btnSearchHint.Text                              = "?";
        btnSearchHint.Cursor                            = Cursors.Help;

        // ═════════════════════════════════════════════════════════
        // AI 안내 레이블 (미리보기 패널 상단 오른쪽 영역)
        // ═════════════════════════════════════════════════════════
        lblAiNotice.Dock          = DockStyle.Fill;
        lblAiNotice.Margin        = new Padding(8, 0, 8, 0);
        lblAiNotice.Text          = "🔔 프로그램 실행 중 AI 모델이 자동 학습을 수행합니다.";
        lblAiNotice.Font          = new Font("Segoe UI", 10.5F, FontStyle.Italic);
        lblAiNotice.TextAlign     = ContentAlignment.MiddleRight;
        lblAiNotice.ForeColor     = Color.FromArgb(120, 140, 165);
        lblAiNotice.AutoEllipsis  = true;
        lblAiNotice.Name          = "lblAiNotice";

        // ═════════════════════════════════════════════════════════
        // 툴바 TableLayoutPanel (5열: [검색창 70%] [? 26px] [✕ 32px] [검색 78px] [AI 안내 30%])
        // ═════════════════════════════════════════════════════════
        tblToolbar.ColumnCount = 5;
        tblToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  70F));  // 검색창
        tblToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26F));  // ? 힌트
        tblToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32F));  // 지우기
        tblToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));  // 검색버튼
        tblToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,  30F));  // AI 안내
        tblToolbar.RowCount = 1;
        tblToolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tblToolbar.Dock    = DockStyle.Fill;
        tblToolbar.Name    = "tblToolbar";
        tblToolbar.Padding = new Padding(6, 7, 6, 7);
        tblToolbar.Controls.Add(txtSearch,     0, 0);
        tblToolbar.Controls.Add(btnSearchHint, 1, 0);
        tblToolbar.Controls.Add(btnClear,      2, 0);
        tblToolbar.Controls.Add(btnSearch,     3, 0);
        tblToolbar.Controls.Add(lblAiNotice,   4, 0);

        // ═════════════════════════════════════════════════════════
        // 옵션 패널
        // ═════════════════════════════════════════════════════════
        chkContent.AutoSize = true; chkContent.Location = new Point(6, 5);
        chkContent.Name = "chkContent"; chkContent.Text = "내용 검색 (F4)"; chkContent.TabIndex = 3;

        lblTypeSep.AutoSize = false; lblTypeSep.Location = new Point(116, 3);
        lblTypeSep.Size = new Size(2, 20); lblTypeSep.Name = "lblTypeSep";
        lblTypeSep.Text = string.Empty; lblTypeSep.BorderStyle = BorderStyle.Fixed3D;

        lblFilter.AutoSize = true; lblFilter.Location = new Point(124, 6);
        lblFilter.Name = "lblFilter"; lblFilter.Text = "유형:";

        chkTypeDoc.AutoSize = true; chkTypeDoc.Location = new Point(162, 5);
        chkTypeDoc.Name = "chkTypeDoc"; chkTypeDoc.Text = "문서"; chkTypeDoc.TabIndex = 4;

        chkTypePdf.AutoSize = true; chkTypePdf.Location = new Point(228, 5);
        chkTypePdf.Name = "chkTypePdf"; chkTypePdf.Text = "PDF"; chkTypePdf.TabIndex = 5;

        chkTypeImage.AutoSize = true; chkTypeImage.Location = new Point(292, 5);
        chkTypeImage.Name = "chkTypeImage"; chkTypeImage.Text = "이미지"; chkTypeImage.TabIndex = 6;

        chkTypeCode.AutoSize = true; chkTypeCode.Location = new Point(368, 5);
        chkTypeCode.Name = "chkTypeCode"; chkTypeCode.Text = "소스코드"; chkTypeCode.TabIndex = 7;

        lblDbInfo.AutoSize = false; lblDbInfo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lblDbInfo.TextAlign = ContentAlignment.MiddleRight; lblDbInfo.Location = new Point(700, 4);
        lblDbInfo.Size = new Size(310, 20); lblDbInfo.Name = "lblDbInfo";
        lblDbInfo.Text = string.Empty; lblDbInfo.ForeColor = SystemColors.GrayText;

        pnlOptions.Controls.AddRange([
            chkContent, lblTypeSep, lblFilter,
            chkTypeDoc, chkTypePdf, chkTypeImage, chkTypeCode,
            lblDbInfo]);
        pnlOptions.Dock = DockStyle.Bottom; pnlOptions.Height = 28; pnlOptions.Name = "pnlOptions";

        // ═════════════════════════════════════════════════════════
        // pnlSearch (툴바 + 옵션)
        // ═════════════════════════════════════════════════════════
        pnlSearch.Controls.AddRange([tblToolbar, pnlOptions]);
        pnlSearch.Dock = DockStyle.Top; pnlSearch.Height = 74; pnlSearch.Name = "pnlSearch";

        // ═════════════════════════════════════════════════════════
        // 스니펫 패널 (미리보기 — SplitContainer Panel2에 배치)
        // ═════════════════════════════════════════════════════════
        lblSnippetIcon.AutoSize = false; lblSnippetIcon.Location = new Point(8, 6);
        lblSnippetIcon.Size = new Size(22, 20); lblSnippetIcon.Text = "📄";
        lblSnippetIcon.Name = "lblSnippetIcon";

        lblSnippetPath.AutoSize = false;
        lblSnippetPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        lblSnippetPath.Location = new Point(40, 8); lblSnippetPath.Name = "lblSnippetPath";
        lblSnippetPath.Size = new Size(320, 16);
        lblSnippetPath.Text = "항목을 선택하면 미리보기가 표시됩니다";
        lblSnippetPath.ForeColor = SystemColors.GrayText;

        btnSnippetClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnSnippetClose.FlatStyle = FlatStyle.Flat;
        btnSnippetClose.FlatAppearance.BorderSize = 0;
        btnSnippetClose.Font = new Font("Segoe UI", 8F);
        btnSnippetClose.Location = new Point(360, 5);
        btnSnippetClose.Name = "btnSnippetClose"; btnSnippetClose.Size = new Size(22, 22);
        btnSnippetClose.Text = "✕"; btnSnippetClose.TabStop = false;

        pnlSnippetBar.Controls.AddRange([lblSnippetIcon, lblSnippetPath, btnSnippetClose]);
        pnlSnippetBar.Dock = DockStyle.Top; pnlSnippetBar.Height = 32;
        pnlSnippetBar.Name = "pnlSnippetBar"; pnlSnippetBar.Padding = new Padding(4, 0, 4, 0);

        rtbSnippet.Dock = DockStyle.Fill; rtbSnippet.BorderStyle = BorderStyle.None;
        rtbSnippet.Font = new Font("Consolas", 9F); rtbSnippet.Name = "rtbSnippet";
        rtbSnippet.ReadOnly = true; rtbSnippet.ScrollBars = RichTextBoxScrollBars.Vertical;
        rtbSnippet.TabStop = false; rtbSnippet.WordWrap = true; rtbSnippet.DetectUrls = false;

        pnlSnippet.Controls.AddRange([rtbSnippet, pnlSnippetBar]);
        pnlSnippet.Dock = DockStyle.Fill;   // Panel2 안에서 Fill
        pnlSnippet.Name = "pnlSnippet";

        // ═════════════════════════════════════════════════════════
        // ListView
        // ═════════════════════════════════════════════════════════
        colFileName.Text = "이름";    colFileName.Width = 240;   // AutoResizeColumns()가 Shown 에서 재조정
        colPath.Text     = "경로";    colPath.Width     = 360;
        colSize.Text     = "파일 크기"; colSize.TextAlign = HorizontalAlignment.Right; colSize.Width = 95;
        colModified.Text = "수정 날짜"; colModified.Width = 138;
        colMatch.Text    = "유형";    colMatch.Width    = 75;

        listResults.Columns.AddRange([colFileName, colPath, colSize, colModified, colMatch]);
        listResults.ContextMenuStrip = cmsResult;
        listResults.Dock = DockStyle.Fill;
        listResults.Font = new Font("Segoe UI", 9.5F);
        listResults.FullRowSelect = true; listResults.GridLines = false;
        listResults.HideSelection = false; listResults.MultiSelect = true;
        listResults.Name = "listResults"; listResults.ShowItemToolTips = true;
        listResults.SmallImageList = imgListSmall; listResults.TabIndex = 0;
        listResults.UseCompatibleStateImageBehavior = false;
        listResults.View = View.Details; listResults.VirtualMode = true;

        imgListSmall.ColorDepth = ColorDepth.Depth32Bit;
        imgListSmall.ImageSize  = new Size(16, 16);
        imgListSmall.TransparentColor = Color.Transparent;

        // ═════════════════════════════════════════════════════════
        // SplitContainer — 좌(결과 목록) / 우(미리보기)
        // Panel1MinSize / Panel2MinSize / SplitterDistance 는
        // 폼 크기 확정 후 Load 이벤트에서 설정 (EndInit 오류 방지)
        // ═════════════════════════════════════════════════════════
        splitPreview.Dock         = DockStyle.Fill;
        splitPreview.Name         = "splitPreview";
        splitPreview.Orientation  = Orientation.Vertical;  // 세로선 = 좌/우 분할
        splitPreview.SplitterWidth = 5;

        splitPreview.Panel1.Controls.Add(listResults);  // 왼쪽: 결과 목록
        splitPreview.Panel2.Controls.Add(pnlSnippet);   // 오른쪽: 미리보기

        // ═════════════════════════════════════════════════════════
        // StatusStrip
        // ═════════════════════════════════════════════════════════
        sslResultCount.AutoSize = true; sslResultCount.Name = "sslResultCount";
        sslResultCount.Text = "준비"; sslResultCount.Padding = new Padding(4, 0, 12, 0);

        sslIndexStatus.AutoSize = true; sslIndexStatus.Name = "sslIndexStatus";
        sslIndexStatus.Spring = true; sslIndexStatus.Text = string.Empty;
        sslIndexStatus.TextAlign = ContentAlignment.MiddleCenter;

        sslDbStats.AutoSize = true; sslDbStats.Name = "sslDbStats";
        sslDbStats.Text = string.Empty; sslDbStats.Padding = new Padding(8, 0, 8, 0);

        sslLastIndexed.AutoSize = true; sslLastIndexed.Name = "sslLastIndexed";
        sslLastIndexed.Text = string.Empty; sslLastIndexed.Padding = new Padding(0, 0, 4, 0);

        statusStrip.Items.AddRange([sslResultCount, sslIndexStatus, sslDbStats, sslLastIndexed]);
        statusStrip.Name = "statusStrip"; statusStrip.SizingGrip = true; statusStrip.TabIndex = 5;

        // ═════════════════════════════════════════════════════════
        // ContextMenuStrip
        // ═════════════════════════════════════════════════════════
        tsmiOpenFile.Name = "tsmiOpenFile"; tsmiOpenFile.Text = "파일 열기(&O)";
        tsmiOpenFile.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        tsmiOpenFile.ShortcutKeyDisplayString = "Enter";

        tsmiOpenFolder.Name = "tsmiOpenFolder"; tsmiOpenFolder.Text = "폴더 열기(&E)";
        tsmiSep1.Name = "tsmiSep1";

        tsmiDeleteFile.Name = "tsmiDeleteFile"; tsmiDeleteFile.Text = "파일/폴더 삭제(&X)";
        tsmiDeleteFile.ForeColor = Color.FromArgb(180, 0, 0);
        tsmiProperties.Name = "tsmiProperties"; tsmiProperties.Text = "속성(&R)";
        tsmiSep2.Name = "tsmiSep2";

        tsmiSelectAll.Name = "tsmiSelectAll"; tsmiSelectAll.Text = "모든 항목 선택(&A)";
        tsmiSelectAll.ShortcutKeyDisplayString = "Ctrl+A";
        tsmiSep3.Name = "tsmiSep3";

        tsmiCopyPath.Name = "tsmiCopyPath"; tsmiCopyPath.Text = "전체 경로 복사(&C)";
        tsmiCopyPath.ShortcutKeyDisplayString = "Ctrl+C";
        tsmiCopyDir.Name = "tsmiCopyDir"; tsmiCopyDir.Text = "디렉터리 복사(&D)";
        tsmiCopyName.Name = "tsmiCopyName"; tsmiCopyName.Text = "파일명 복사(&F)";
        tsmiSep4.Name = "tsmiSep4";
        tsmiDeleteFromIndex.Name = "tsmiDeleteFromIndex"; tsmiDeleteFromIndex.Text = "인덱스에서 제거(&L)";
        tsmiDeleteFromIndex.ForeColor = Color.FromArgb(160, 60, 0);

        cmsResult.Items.AddRange([
            tsmiOpenFile, tsmiOpenFolder, tsmiSep1,
            tsmiDeleteFile, tsmiProperties, tsmiSep2,
            tsmiSelectAll, tsmiSep3,
            tsmiCopyPath, tsmiCopyDir, tsmiCopyName, tsmiSep4,
            tsmiDeleteFromIndex]);
        cmsResult.Name = "cmsResult";

        // ═════════════════════════════════════════════════════════
        // 첫 실행 오버레이 (기본 hidden)  — 카드 크기 520 × 256
        // ═════════════════════════════════════════════════════════

        // 🤖 AI 아이콘
        lblOverlayIcon.AutoSize   = false;
        lblOverlayIcon.Location   = new Point(0, 18);
        lblOverlayIcon.Size       = new Size(520, 50);
        lblOverlayIcon.Text       = "🤖";
        lblOverlayIcon.Font       = new Font("Segoe UI Emoji", 26F);
        lblOverlayIcon.TextAlign  = ContentAlignment.MiddleCenter;
        lblOverlayIcon.Name       = "lblOverlayIcon";
        lblOverlayIcon.BackColor  = Color.Transparent;

        // 메인 제목
        lblOverlayTitle.AutoSize  = false;
        lblOverlayTitle.Location  = new Point(0, 72);
        lblOverlayTitle.Size      = new Size(520, 32);
        lblOverlayTitle.Text      = "AI가 내 컴퓨터를 학습하고 있습니다";
        lblOverlayTitle.Font      = new Font("Segoe UI", 14F, FontStyle.Bold);
        lblOverlayTitle.TextAlign = ContentAlignment.MiddleCenter;
        lblOverlayTitle.Name      = "lblOverlayTitle";
        lblOverlayTitle.BackColor = Color.Transparent;

        // 설명
        lblOverlayDesc.AutoSize   = false;
        lblOverlayDesc.Location   = new Point(0, 108);
        lblOverlayDesc.Size       = new Size(520, 22);
        lblOverlayDesc.Text       = "파일 내용을 분석하고 검색 인덱스를 구축하고 있습니다";
        lblOverlayDesc.Font       = new Font("Segoe UI", 9.5F);
        lblOverlayDesc.TextAlign  = ContentAlignment.MiddleCenter;
        lblOverlayDesc.ForeColor  = Color.FromArgb(80, 100, 130);
        lblOverlayDesc.Name       = "lblOverlayDesc";
        lblOverlayDesc.BackColor  = Color.Transparent;

        // 진행 바
        pbOverlay.Location              = new Point(40, 148);
        pbOverlay.Size                  = new Size(440, 12);
        pbOverlay.Style                 = ProgressBarStyle.Marquee;
        pbOverlay.MarqueeAnimationSpeed = 20;
        pbOverlay.Name                  = "pbOverlay";

        // 진행 텍스트 (파일 수 / 퍼센트)
        lblOverlayProgress.AutoSize  = false;
        lblOverlayProgress.Location  = new Point(0, 168);
        lblOverlayProgress.Size      = new Size(520, 20);
        lblOverlayProgress.Text      = "파일 목록 수집 중...";
        lblOverlayProgress.Font      = new Font("Segoe UI", 8.5F);
        lblOverlayProgress.TextAlign = ContentAlignment.MiddleCenter;
        lblOverlayProgress.ForeColor = Color.FromArgb(110, 125, 148);
        lblOverlayProgress.Name      = "lblOverlayProgress";
        lblOverlayProgress.BackColor = Color.Transparent;

        // 안내 메모
        lblOverlayNote.AutoSize   = false;
        lblOverlayNote.Location   = new Point(0, 196);
        lblOverlayNote.Size       = new Size(520, 36);
        lblOverlayNote.Text       = "✅ 최초 실행 시 한 번만 수행됩니다.\r\n    이후에는 자동으로 업데이트됩니다.";
        lblOverlayNote.Font       = new Font("Segoe UI", 8.5F, FontStyle.Italic);
        lblOverlayNote.TextAlign  = ContentAlignment.MiddleCenter;
        lblOverlayNote.ForeColor  = Color.FromArgb(140, 160, 190);
        lblOverlayNote.Name       = "lblOverlayNote";
        lblOverlayNote.BackColor  = Color.Transparent;

        // 닫기 버튼 (인덱싱 완료 후 표시)
        btnOverlayClose.AutoSize  = false;
        btnOverlayClose.Location  = new Point(170, 242);
        btnOverlayClose.Size      = new Size(180, 38);
        btnOverlayClose.Text      = "닫기";
        btnOverlayClose.Font      = new Font("Segoe UI", 10F, FontStyle.Bold);
        btnOverlayClose.BackColor = Color.FromArgb(25, 118, 210);
        btnOverlayClose.ForeColor = Color.White;
        btnOverlayClose.FlatStyle = FlatStyle.Flat;
        btnOverlayClose.FlatAppearance.BorderSize  = 0;
        btnOverlayClose.FlatAppearance.MouseOverBackColor  = Color.FromArgb(21, 101, 192);
        btnOverlayClose.FlatAppearance.MouseDownBackColor  = Color.FromArgb(13, 71, 161);
        btnOverlayClose.Cursor    = Cursors.Hand;
        btnOverlayClose.Name      = "btnOverlayClose";
        btnOverlayClose.TabIndex  = 0;
        btnOverlayClose.Visible   = false;   // 인덱싱 중에는 숨김

        pnlOverlayCard.Size           = new Size(520, 298);
        pnlOverlayCard.BackColor      = Color.White;
        pnlOverlayCard.Name           = "pnlOverlayCard";
        pnlOverlayCard.Controls.AddRange([
            lblOverlayIcon, lblOverlayTitle, lblOverlayDesc,
            pbOverlay, lblOverlayProgress, lblOverlayNote,
            btnOverlayClose]);

        pnlOverlay.BackColor          = Color.FromArgb(30, 35, 50);
        pnlOverlay.Name               = "pnlOverlay";
        pnlOverlay.Visible            = false;
        pnlOverlay.Controls.Add(pnlOverlayCard);

        // ═════════════════════════════════════════════════════════
        // MainForm 컨트롤 등록
        // ═════════════════════════════════════════════════════════
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        ClientSize          = new Size(1024, 680);
        Font                = new Font("Segoe UI", 9F);
        MainMenuStrip       = menuStrip;
        MinimumSize         = new Size(720, 480);
        Name                = "MainForm";
        StartPosition       = FormStartPosition.CenterScreen;
        Text                = "SFM — Smart File Manager";

        // 도킹 순서 (나중에 추가된 컨트롤이 먼저 도킹됨)
        Controls.Add(splitPreview);  // Fill — 결과목록+미리보기 컨테이너
        Controls.Add(pnlSearch);     // Top
        Controls.Add(menuStrip);     // Top (최상단)
        Controls.Add(statusStrip);   // Bottom (맨 아래)
        Controls.Add(pnlOverlay);    // 오버레이 — BringToFront() 로 최상위 표시

        // ─────────────────────────────────────────────────────────
        splitPreview.Panel1.ResumeLayout(false);
        splitPreview.Panel2.ResumeLayout(false);
        splitPreview.ResumeLayout(false);

        menuStrip.ResumeLayout(false);
        menuStrip.PerformLayout();
        tblToolbar.ResumeLayout(false);
        pnlSearch.ResumeLayout(false);
        pnlOptions.ResumeLayout(false);
        pnlSnippet.ResumeLayout(false);
        pnlSnippetBar.ResumeLayout(false);
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    // ── 컨트롤 선언 ───────────────────────────────────────────────
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
    private ToolStripSeparator   tsmiSearchSep1      = null!;
    private ToolStripMenuItem    tsmiSearchClear     = null!;
    private ToolStripMenuItem    tsmiTools           = null!;
    private ToolStripMenuItem    tsmiToolsReindex    = null!;
    private ToolStripSeparator   tsmiToolsSep2       = null!;
    private ToolStripMenuItem    tsmiToolsVacuum     = null!;
    private ToolStripSeparator   tsmiToolsSep3       = null!;
    private ToolStripMenuItem    tsmiToolsResetIndex = null!;
    private ToolStripMenuItem    tsmiHelp            = null!;
    private ToolStripMenuItem    tsmiHelpAbout       = null!;

    private Panel                pnlSearch           = null!;
    private TableLayoutPanel     tblToolbar          = null!;
    private TextBox              txtSearch           = null!;
    private Button               btnSearchHint       = null!;
    private Button               btnClear            = null!;
    private Button               btnSearch           = null!;
    private Label                lblAiNotice         = null!;

    private Panel                pnlOptions          = null!;
    private CheckBox             chkContent          = null!;
    private Label                lblTypeSep          = null!;
    private Label                lblFilter           = null!;
    private CheckBox             chkTypeDoc          = null!;
    private CheckBox             chkTypePdf          = null!;
    private CheckBox             chkTypeImage        = null!;
    private CheckBox             chkTypeCode         = null!;
    private Label                lblDbInfo           = null!;

    private Panel                pnlSnippet          = null!;
    private Panel                pnlSnippetBar       = null!;
    private Label                lblSnippetIcon      = null!;
    private Label                lblSnippetPath      = null!;
    private Button               btnSnippetClose     = null!;
    private RichTextBox          rtbSnippet          = null!;

    private SplitContainer       splitPreview        = null!;

    private ExtListView          listResults         = null!;
    private StatusStrip          statusStrip         = null!;
    private ToolStripStatusLabel sslResultCount      = null!;
    private ToolStripStatusLabel sslIndexStatus      = null!;
    private ToolStripStatusLabel sslDbStats          = null!;
    private ToolStripStatusLabel sslLastIndexed      = null!;

    private ContextMenuStrip     cmsResult           = null!;
    private ToolStripMenuItem    tsmiOpenFile        = null!;
    private ToolStripMenuItem    tsmiOpenFolder      = null!;
    private ToolStripSeparator   tsmiSep1            = null!;
    private ToolStripMenuItem    tsmiDeleteFile      = null!;
    private ToolStripMenuItem    tsmiProperties      = null!;
    private ToolStripSeparator   tsmiSep2            = null!;
    private ToolStripMenuItem    tsmiSelectAll       = null!;
    private ToolStripSeparator   tsmiSep3            = null!;
    private ToolStripMenuItem    tsmiCopyPath        = null!;
    private ToolStripMenuItem    tsmiCopyDir         = null!;
    private ToolStripMenuItem    tsmiCopyName        = null!;
    private ToolStripSeparator   tsmiSep4            = null!;
    private ToolStripMenuItem    tsmiDeleteFromIndex = null!;

    private Panel                pnlOverlay          = null!;
    private Panel                pnlOverlayCard      = null!;
    private Label                lblOverlayIcon      = null!;
    private Label                lblOverlayTitle     = null!;
    private Label                lblOverlayDesc      = null!;
    private ProgressBar          pbOverlay           = null!;
    private Label                lblOverlayProgress  = null!;
    private Label                lblOverlayNote      = null!;
    private Button               btnOverlayClose     = null!;

    private ColumnHeader         colFileName         = null!;
    private ColumnHeader         colPath             = null!;
    private ColumnHeader         colSize             = null!;
    private ColumnHeader         colModified         = null!;
    private ColumnHeader         colMatch            = null!;
    private ImageList            imgListSmall        = null!;
}
