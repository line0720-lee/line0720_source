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

        // ── 툴바 패널 ─────────────────────────────────────────────
        pnlSearch           = new Panel();
        tblToolbar          = new TableLayoutPanel();
        cmbLocation         = new ComboBox();
        txtSearch           = new TextBox();
        btnClear            = new Button();
        btnSearch           = new Button();

        // ── 옵션 패널 ─────────────────────────────────────────────
        pnlOptions          = new Panel();
        lblFilter           = new Label();
        cmbFilter           = new ComboBox();
        chkContent          = new CheckBox();
        chkCase             = new CheckBox();
        chkWholeWord        = new CheckBox();
        lblDbInfo           = new Label();

        // ── 스니펫 패널 ───────────────────────────────────────────
        pnlSnippet          = new Panel();
        pnlSnippetBar       = new Panel();
        lblSnippetIcon      = new Label();
        lblSnippetPath      = new Label();
        btnSnippetClose     = new Button();
        rtbSnippet          = new RichTextBox();

        // ── 결과 목록 ─────────────────────────────────────────────
        listResults         = new ListView();
        colFileName         = new ColumnHeader();
        colPath             = new ColumnHeader();
        colSize             = new ColumnHeader();
        colModified         = new ColumnHeader();
        colMatch            = new ColumnHeader();
        imgListSmall        = new ImageList(components);

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
        tsmiMoveFile        = new ToolStripMenuItem();
        tsmiDeleteFile      = new ToolStripMenuItem();
        tsmiProperties      = new ToolStripMenuItem();
        tsmiSep2            = new ToolStripSeparator();
        tsmiSelectAll       = new ToolStripMenuItem();
        tsmiSelectNone      = new ToolStripMenuItem();
        tsmiInvertSelect    = new ToolStripMenuItem();
        tsmiSep3            = new ToolStripSeparator();
        tsmiCopyPath        = new ToolStripMenuItem();
        tsmiCopyDir         = new ToolStripMenuItem();
        tsmiCopyName        = new ToolStripMenuItem();
        tsmiSaveAsText      = new ToolStripMenuItem();
        tsmiSep4            = new ToolStripSeparator();
        tsmiDeleteFromIndex = new ToolStripMenuItem();

        // ─────────────────────────────────────────────────────────
        // SuspendLayout
        // ─────────────────────────────────────────────────────────
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
        menuStrip.Location  = new Point(0, 0);
        menuStrip.Name      = "menuStrip";
        menuStrip.Size      = new Size(1024, 24);
        menuStrip.TabIndex  = 0;

        // ── 파일 메뉴 ─────────────────────────────────────────────
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

        // ── 보기 메뉴 ─────────────────────────────────────────────
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

        // ── 검색 메뉴 ─────────────────────────────────────────────
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

        tsmiSearchClear.Name                     = "tsmiSearchClear";
        tsmiSearchClear.Text                     = "검색어 지우기(&L)";
        tsmiSearchClear.ShortcutKeyDisplayString = "Esc";

        // ── 도구 메뉴 ─────────────────────────────────────────────
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

        // ── 도움말 메뉴 ───────────────────────────────────────────
        tsmiHelp.DropDownItems.AddRange([tsmiHelpAbout]);
        tsmiHelp.Name = "tsmiHelp";
        tsmiHelp.Text = "도움말(&H)";

        tsmiHelpAbout.Name = "tsmiHelpAbout";
        tsmiHelpAbout.Text = "SFM 정보(&A)…";

        // ═════════════════════════════════════════════════════════
        // 위치 드롭다운 (cmbLocation)
        // ═════════════════════════════════════════════════════════
        cmbLocation.Anchor          = AnchorStyles.Left | AnchorStyles.Right;
        cmbLocation.DropDownStyle   = ComboBoxStyle.DropDownList;
        cmbLocation.FlatStyle       = FlatStyle.Flat;
        cmbLocation.Font            = new Font("Segoe UI", 9.5F);
        cmbLocation.Margin          = new Padding(0, 0, 4, 0);
        cmbLocation.Name            = "cmbLocation";
        cmbLocation.TabIndex        = 1;

        // ═════════════════════════════════════════════════════════
        // 검색창 (txtSearch)
        // ═════════════════════════════════════════════════════════
        txtSearch.Anchor          = AnchorStyles.Left | AnchorStyles.Right;
        txtSearch.BorderStyle     = BorderStyle.FixedSingle;
        txtSearch.Font            = new Font("Segoe UI", 11F);
        txtSearch.Margin          = new Padding(0, 0, 0, 0);
        txtSearch.Name            = "txtSearch";
        txtSearch.PlaceholderText = "검색어를 입력하세요…";
        txtSearch.TabIndex        = 2;

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
        btnSearch.Dock                              = DockStyle.Fill;
        btnSearch.FlatStyle                         = FlatStyle.Flat;
        btnSearch.FlatAppearance.BorderColor        = Color.FromArgb(0, 90, 180);
        btnSearch.BackColor                         = Color.FromArgb(0, 120, 212);
        btnSearch.ForeColor                         = Color.White;
        btnSearch.Font                              = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        btnSearch.Margin                            = new Padding(0, 0, 0, 0);
        btnSearch.Name                              = "btnSearch";
        btnSearch.TabIndex                          = 3;
        btnSearch.Text                              = "검색";

        // ═════════════════════════════════════════════════════════
        // 툴바 TableLayoutPanel (tblToolbar)
        // ═════════════════════════════════════════════════════════
        tblToolbar.ColumnCount = 4;
        tblToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 195F)); // 위치
        tblToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));  // 검색
        tblToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32F));  // 지우기
        tblToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));  // 검색버튼
        tblToolbar.RowCount = 1;
        tblToolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tblToolbar.Dock    = DockStyle.Fill;
        tblToolbar.Name    = "tblToolbar";
        tblToolbar.Padding = new Padding(4, 7, 6, 7);
        tblToolbar.Controls.Add(cmbLocation, 0, 0);
        tblToolbar.Controls.Add(txtSearch,   1, 0);
        tblToolbar.Controls.Add(btnClear,    2, 0);
        tblToolbar.Controls.Add(btnSearch,   3, 0);

        // ═════════════════════════════════════════════════════════
        // 옵션 패널 (pnlOptions)
        // ═════════════════════════════════════════════════════════
        lblFilter.AutoSize  = true;
        lblFilter.Location  = new Point(4, 6);
        lblFilter.Name      = "lblFilter";
        lblFilter.Text      = "유형:";

        cmbFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbFilter.Items.AddRange(["전체", "문서", "PDF", "이미지", "소스코드"]);
        cmbFilter.Location      = new Point(38, 3);
        cmbFilter.Name          = "cmbFilter";
        cmbFilter.SelectedIndex = 0;
        cmbFilter.Size          = new Size(90, 23);
        cmbFilter.TabIndex      = 4;
        cmbFilter.FlatStyle     = FlatStyle.Popup;

        chkContent.AutoSize  = true;
        chkContent.Location  = new Point(140, 5);
        chkContent.Name      = "chkContent";
        chkContent.Text      = "내용 검색 (F4)";
        chkContent.TabIndex  = 5;

        chkCase.AutoSize  = true;
        chkCase.Location  = new Point(255, 5);
        chkCase.Name      = "chkCase";
        chkCase.Text      = "대소문자";
        chkCase.TabIndex  = 6;

        chkWholeWord.AutoSize  = true;
        chkWholeWord.Location  = new Point(342, 5);
        chkWholeWord.Name      = "chkWholeWord";
        chkWholeWord.Text      = "전체 단어";
        chkWholeWord.TabIndex  = 7;

        lblDbInfo.AutoSize   = false;
        lblDbInfo.Anchor     = AnchorStyles.Top | AnchorStyles.Right;
        lblDbInfo.TextAlign  = ContentAlignment.MiddleRight;
        lblDbInfo.Location   = new Point(700, 4);
        lblDbInfo.Size       = new Size(310, 20);
        lblDbInfo.Name       = "lblDbInfo";
        lblDbInfo.Text       = string.Empty;
        lblDbInfo.ForeColor  = SystemColors.GrayText;

        pnlOptions.Controls.AddRange([lblFilter, cmbFilter, chkContent, chkCase, chkWholeWord, lblDbInfo]);
        pnlOptions.Dock   = DockStyle.Bottom;
        pnlOptions.Height = 28;
        pnlOptions.Name   = "pnlOptions";

        // ═════════════════════════════════════════════════════════
        // 검색 패널 (pnlSearch) — 툴바 + 옵션
        // ═════════════════════════════════════════════════════════
        pnlSearch.Controls.AddRange([tblToolbar, pnlOptions]);
        pnlSearch.Dock   = DockStyle.Top;
        pnlSearch.Height = 74;
        pnlSearch.Name   = "pnlSearch";

        // ═════════════════════════════════════════════════════════
        // 스니펫 패널
        // ═════════════════════════════════════════════════════════
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
        pnlSnippetBar.Dock    = DockStyle.Top;
        pnlSnippetBar.Height  = 24;
        pnlSnippetBar.Name    = "pnlSnippetBar";
        pnlSnippetBar.Padding = new Padding(2, 0, 2, 0);

        rtbSnippet.Dock        = DockStyle.Fill;
        rtbSnippet.BorderStyle = BorderStyle.None;
        rtbSnippet.Font        = new Font("Consolas", 9F);
        rtbSnippet.Name        = "rtbSnippet";
        rtbSnippet.ReadOnly    = true;
        rtbSnippet.ScrollBars  = RichTextBoxScrollBars.Vertical;
        rtbSnippet.TabStop     = false;
        rtbSnippet.WordWrap    = true;
        rtbSnippet.DetectUrls  = false;

        pnlSnippet.Controls.AddRange([rtbSnippet, pnlSnippetBar]);
        pnlSnippet.Dock   = DockStyle.Bottom;
        pnlSnippet.Height = 110;
        pnlSnippet.Name   = "pnlSnippet";

        // ═════════════════════════════════════════════════════════
        // ListView 컬럼
        // ═════════════════════════════════════════════════════════
        colFileName.Text  = "이름";
        colFileName.Width = 240;

        colPath.Text  = "경로";
        colPath.Width = 340;

        colSize.Text      = "파일 크기";
        colSize.TextAlign = HorizontalAlignment.Right;
        colSize.Width     = 90;

        colModified.Text  = "수정 날짜";
        colModified.Width = 145;

        colMatch.Text  = "유형";
        colMatch.Width = 80;

        // ═════════════════════════════════════════════════════════
        // listResults
        // ═════════════════════════════════════════════════════════
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

        // ═════════════════════════════════════════════════════════
        // StatusStrip
        // ═════════════════════════════════════════════════════════
        sslResultCount.AutoSize = true;
        sslResultCount.Name     = "sslResultCount";
        sslResultCount.Text     = "준비";
        sslResultCount.Padding  = new Padding(4, 0, 12, 0);

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

        // ═════════════════════════════════════════════════════════
        // ContextMenuStrip  (Wise JetSearch 스타일)
        // ═════════════════════════════════════════════════════════
        tsmiOpenFile.Name    = "tsmiOpenFile";
        tsmiOpenFile.Text    = "파일 열기(&O)";
        tsmiOpenFile.Font    = new Font("Segoe UI", 9F, FontStyle.Bold);
        tsmiOpenFile.ShortcutKeyDisplayString = "Enter";

        tsmiOpenFolder.Name = "tsmiOpenFolder";
        tsmiOpenFolder.Text = "폴더 열기(&E)";

        tsmiSep1.Name = "tsmiSep1";

        tsmiMoveFile.Name = "tsmiMoveFile";
        tsmiMoveFile.Text = "이동하기(&M)…";

        tsmiDeleteFile.Name      = "tsmiDeleteFile";
        tsmiDeleteFile.Text      = "파일/폴더 삭제(&X)";
        tsmiDeleteFile.ForeColor = Color.FromArgb(180, 0, 0);

        tsmiProperties.Name = "tsmiProperties";
        tsmiProperties.Text = "속성(&R)";

        tsmiSep2.Name = "tsmiSep2";

        tsmiSelectAll.Name = "tsmiSelectAll";
        tsmiSelectAll.Text = "모든 항목 선택(&A)";
        tsmiSelectAll.ShortcutKeyDisplayString = "Ctrl+A";

        tsmiSelectNone.Name = "tsmiSelectNone";
        tsmiSelectNone.Text = "선택 없음(&N)";

        tsmiInvertSelect.Name = "tsmiInvertSelect";
        tsmiInvertSelect.Text = "선택 반전(&I)";

        tsmiSep3.Name = "tsmiSep3";

        tsmiCopyPath.Name = "tsmiCopyPath";
        tsmiCopyPath.Text = "전체 경로 복사(&C)";
        tsmiCopyPath.ShortcutKeyDisplayString = "Ctrl+C";

        tsmiCopyDir.Name = "tsmiCopyDir";
        tsmiCopyDir.Text = "디렉터리 복사(&D)";

        tsmiCopyName.Name = "tsmiCopyName";
        tsmiCopyName.Text = "파일명 복사(&F)";

        tsmiSaveAsText.Name = "tsmiSaveAsText";
        tsmiSaveAsText.Text = "텍스트 파일로 저장(&S)…";

        tsmiSep4.Name = "tsmiSep4";

        tsmiDeleteFromIndex.Name      = "tsmiDeleteFromIndex";
        tsmiDeleteFromIndex.Text      = "인덱스에서 제거(&L)";
        tsmiDeleteFromIndex.ForeColor = Color.FromArgb(160, 60, 0);

        cmsResult.Items.AddRange([
            tsmiOpenFile, tsmiOpenFolder, tsmiSep1,
            tsmiMoveFile, tsmiDeleteFile, tsmiProperties, tsmiSep2,
            tsmiSelectAll, tsmiSelectNone, tsmiInvertSelect, tsmiSep3,
            tsmiCopyPath, tsmiCopyDir, tsmiCopyName, tsmiSaveAsText, tsmiSep4,
            tsmiDeleteFromIndex]);
        cmsResult.Name = "cmsResult";

        // ═════════════════════════════════════════════════════════
        // ImageList
        // ═════════════════════════════════════════════════════════
        imgListSmall.ColorDepth       = ColorDepth.Depth32Bit;
        imgListSmall.ImageSize        = new Size(16, 16);
        imgListSmall.TransparentColor = Color.Transparent;

        // ═════════════════════════════════════════════════════════
        // MainForm
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

        // 도킹 순서: Fill → Bottom → Top (나중에 추가된 Top이 최상단)
        Controls.Add(listResults);   // Fill
        Controls.Add(pnlSnippet);    // Bottom
        Controls.Add(pnlSearch);     // Top (menuStrip 바로 아래)
        Controls.Add(menuStrip);     // Top (최상단)
        Controls.Add(statusStrip);   // Bottom

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
    private TableLayoutPanel     tblToolbar          = null!;
    private ComboBox             cmbLocation         = null!;
    private TextBox              txtSearch           = null!;
    private Button               btnClear            = null!;
    private Button               btnSearch           = null!;

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
    private ToolStripMenuItem    tsmiOpenFolder      = null!;
    private ToolStripSeparator   tsmiSep1            = null!;
    private ToolStripMenuItem    tsmiMoveFile        = null!;
    private ToolStripMenuItem    tsmiDeleteFile      = null!;
    private ToolStripMenuItem    tsmiProperties      = null!;
    private ToolStripSeparator   tsmiSep2            = null!;
    private ToolStripMenuItem    tsmiSelectAll       = null!;
    private ToolStripMenuItem    tsmiSelectNone      = null!;
    private ToolStripMenuItem    tsmiInvertSelect    = null!;
    private ToolStripSeparator   tsmiSep3            = null!;
    private ToolStripMenuItem    tsmiCopyPath        = null!;
    private ToolStripMenuItem    tsmiCopyDir         = null!;
    private ToolStripMenuItem    tsmiCopyName        = null!;
    private ToolStripMenuItem    tsmiSaveAsText      = null!;
    private ToolStripSeparator   tsmiSep4            = null!;
    private ToolStripMenuItem    tsmiDeleteFromIndex = null!;

    private ColumnHeader         colFileName         = null!;
    private ColumnHeader         colPath             = null!;
    private ColumnHeader         colSize             = null!;
    private ColumnHeader         colModified         = null!;
    private ColumnHeader         colMatch            = null!;
    private ImageList            imgListSmall        = null!;
}
