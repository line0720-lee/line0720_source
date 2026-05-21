namespace SFM.UI;

partial class IndexSettingsForm
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

        // ── 최상위 ───────────────────────────────────────────
        tabCtrl         = new TabControl();
        tabPaths        = new TabPage();
        tabSearch       = new TabPage();
        tabRun          = new TabPage();
        btnSave         = new Button();
        btnCancel       = new Button();

        // ── Tab 1: 인덱싱 경로 ───────────────────────────────
        lblPathsHint    = new Label();
        lstPaths        = new ListBox();
        btnAddPath      = new Button();
        btnRemovePath   = new Button();

        // ── Tab 2: 검색 옵션 ─────────────────────────────────
        grpContent      = new GroupBox();
        chkEnableContent     = new CheckBox();
        lblContentDesc       = new Label();
        lblReindexWarning    = new Label();
        lblTotalFiles        = new Label();
        lblContentIndexed    = new Label();
        pbContentRatio       = new ProgressBar();
        btnRunContent        = new Button();
        btnClearContent      = new Button();

        grpSearchOpts   = new GroupBox();
        lblMaxResults        = new Label();
        nudMaxResults        = new NumericUpDown();
        chkShowSnippet       = new CheckBox();

        // ── Tab 3: 인덱싱 실행 ───────────────────────────────
        grpExecution    = new GroupBox();
        btnStartIndex        = new Button();
        btnCancelIndex       = new Button();
        pbIndexProgress      = new ProgressBar();
        lblIndexPhase        = new Label();
        lblCurrentFile       = new Label();

        grpDanger       = new GroupBox();
        btnResetAll          = new Button();
        lblResetHint         = new Label();

        grpStats        = new GroupBox();
        lblStatTotal         = new Label();
        lblStatContent       = new Label();
        lblStatDbSize        = new Label();
        lblStatLastUpdate    = new Label();

        // ── SuspendLayout ────────────────────────────────────
        tabCtrl.SuspendLayout();
        tabPaths.SuspendLayout();
        tabSearch.SuspendLayout();
        tabRun.SuspendLayout();
        grpContent.SuspendLayout();
        grpSearchOpts.SuspendLayout();
        grpExecution.SuspendLayout();
        grpDanger.SuspendLayout();
        grpStats.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)nudMaxResults).BeginInit();
        SuspendLayout();

        // ═════════════════════════════════════════════════════
        // Tab 1 — 인덱싱 경로
        // ═════════════════════════════════════════════════════

        lblPathsHint.AutoSize  = true;
        lblPathsHint.Location  = new Point(8, 10);
        lblPathsHint.Text      = "감시할 폴더 목록 (변경 후 저장 시 FileWatcher 자동 갱신):";

        lstPaths.Anchor        = AnchorStyles.Top | AnchorStyles.Bottom
                               | AnchorStyles.Left | AnchorStyles.Right;
        lstPaths.Location      = new Point(8, 32);
        lstPaths.Name          = "lstPaths";
        lstPaths.Size          = new Size(570, 350);
        lstPaths.TabIndex      = 0;

        btnAddPath.Anchor      = AnchorStyles.Bottom | AnchorStyles.Left;
        btnAddPath.Location    = new Point(8, 393);
        btnAddPath.Size        = new Size(120, 28);
        btnAddPath.Text        = "+ 경로 추가";
        btnAddPath.TabIndex    = 1;
        btnAddPath.UseVisualStyleBackColor = true;

        btnRemovePath.Anchor   = AnchorStyles.Bottom | AnchorStyles.Left;
        btnRemovePath.Location = new Point(136, 393);
        btnRemovePath.Size     = new Size(120, 28);
        btnRemovePath.Text     = "− 경로 제거";
        btnRemovePath.TabIndex = 2;
        btnRemovePath.UseVisualStyleBackColor = true;

        tabPaths.Controls.AddRange([lblPathsHint, lstPaths, btnAddPath, btnRemovePath]);
        tabPaths.Name   = "tabPaths";
        tabPaths.Padding = new Padding(4);
        tabPaths.Text   = "인덱싱 경로";
        tabPaths.UseVisualStyleBackColor = true;

        // ═════════════════════════════════════════════════════
        // Tab 2 — 검색 옵션
        // ═════════════════════════════════════════════════════

        // ── GroupBox: 내용 인덱싱 ─────────────────────────────
        chkEnableContent.AutoSize  = true;
        chkEnableContent.Location  = new Point(12, 24);
        chkEnableContent.Name      = "chkEnableContent";
        chkEnableContent.Text      = "파일 내용 인덱싱 활성화 (텍스트·문서·소스코드 내용 검색 가능)";
        chkEnableContent.TabIndex  = 0;

        lblContentDesc.AutoSize    = false;
        lblContentDesc.Location    = new Point(28, 50);
        lblContentDesc.Size        = new Size(544, 46);
        lblContentDesc.ForeColor   = Color.DimGray;
        lblContentDesc.Text        =
            "활성화 시: 파일 내용을 DB에 저장 → 키워드 내용 검색 지원 (DB 용량↑, 인덱싱 시간↑)\r\n" +
            "비활성화 시: 파일명·메타정보만 저장 → 빠른 인덱싱, 작은 DB 용량";

        lblReindexWarning.AutoSize  = true;
        lblReindexWarning.ForeColor = Color.OrangeRed;
        lblReindexWarning.Font      = new Font("Segoe UI", 9F, FontStyle.Bold);
        lblReindexWarning.Location  = new Point(12, 102);
        lblReindexWarning.Text      = "⚠  설정 변경 후 재인덱싱이 필요합니다.";
        lblReindexWarning.Visible   = false;

        lblTotalFiles.AutoSize    = true;
        lblTotalFiles.Location    = new Point(12, 130);
        lblTotalFiles.Text        = "전체 파일: —";

        lblContentIndexed.AutoSize = true;
        lblContentIndexed.Location = new Point(12, 150);
        lblContentIndexed.Text     = "내용 인덱싱 완료: —";

        pbContentRatio.Location    = new Point(12, 174);
        pbContentRatio.Size        = new Size(552, 14);
        pbContentRatio.Minimum     = 0;
        pbContentRatio.Maximum     = 100;
        pbContentRatio.Value       = 0;

        btnRunContent.Location     = new Point(12, 200);
        btnRunContent.Size         = new Size(196, 28);
        btnRunContent.Text         = "▶  내용 인덱싱만 추가 실행";
        btnRunContent.TabIndex     = 1;
        btnRunContent.UseVisualStyleBackColor = true;

        btnClearContent.Location   = new Point(216, 200);
        btnClearContent.Size       = new Size(178, 28);
        btnClearContent.Text       = "🗑  내용 인덱스만 초기화";
        btnClearContent.TabIndex   = 2;
        btnClearContent.UseVisualStyleBackColor = true;

        grpContent.Controls.AddRange([
            chkEnableContent, lblContentDesc, lblReindexWarning,
            lblTotalFiles, lblContentIndexed, pbContentRatio,
            btnRunContent, btnClearContent]);
        grpContent.Location = new Point(8, 6);
        grpContent.Name     = "grpContent";
        grpContent.Size     = new Size(576, 242);
        grpContent.Text     = "내용 인덱싱";
        grpContent.TabStop  = false;

        // ── GroupBox: 검색 결과 옵션 ──────────────────────────
        lblMaxResults.AutoSize  = true;
        lblMaxResults.Location  = new Point(12, 26);
        lblMaxResults.Text      = "검색 결과 최대 개수:";

        nudMaxResults.Location  = new Point(154, 23);
        nudMaxResults.Size      = new Size(78, 23);
        nudMaxResults.Minimum   = 50;
        nudMaxResults.Maximum   = 1000;
        nudMaxResults.Increment = 50;
        nudMaxResults.Value     = 200;
        nudMaxResults.TabIndex  = 3;

        chkShowSnippet.AutoSize = true;
        chkShowSnippet.Location = new Point(250, 26);
        chkShowSnippet.Text     = "내용 발췌(스니펫) 표시";
        chkShowSnippet.TabIndex = 4;

        grpSearchOpts.Controls.AddRange([lblMaxResults, nudMaxResults, chkShowSnippet]);
        grpSearchOpts.Location = new Point(8, 256);
        grpSearchOpts.Name     = "grpSearchOpts";
        grpSearchOpts.Size     = new Size(576, 62);
        grpSearchOpts.Text     = "검색 결과";
        grpSearchOpts.TabStop  = false;

        tabSearch.Controls.AddRange([grpContent, grpSearchOpts]);
        tabSearch.Name    = "tabSearch";
        tabSearch.Padding = new Padding(4);
        tabSearch.Text    = "검색 옵션";
        tabSearch.UseVisualStyleBackColor = true;

        // ═════════════════════════════════════════════════════
        // Tab 3 — 인덱싱 실행
        // ═════════════════════════════════════════════════════

        // ── GroupBox: 실행 ────────────────────────────────────
        btnStartIndex.Location   = new Point(12, 26);
        btnStartIndex.Size       = new Size(145, 30);
        btnStartIndex.Text       = "▶  지금 인덱싱 시작";
        btnStartIndex.TabIndex   = 0;
        btnStartIndex.UseVisualStyleBackColor = true;

        btnCancelIndex.Enabled   = false;
        btnCancelIndex.Location  = new Point(164, 26);
        btnCancelIndex.Size      = new Size(80, 30);
        btnCancelIndex.Text      = "■ 취소";
        btnCancelIndex.TabIndex  = 1;
        btnCancelIndex.UseVisualStyleBackColor = true;

        pbIndexProgress.Location = new Point(12, 68);
        pbIndexProgress.Size     = new Size(552, 16);
        pbIndexProgress.Minimum  = 0;
        pbIndexProgress.Maximum  = 100;
        pbIndexProgress.Style    = ProgressBarStyle.Continuous;

        lblIndexPhase.AutoSize   = true;
        lblIndexPhase.Location   = new Point(12, 92);
        lblIndexPhase.Text       = "대기 중";

        lblCurrentFile.AutoSize    = false;
        lblCurrentFile.AutoEllipsis = true;
        lblCurrentFile.Location    = new Point(12, 114);
        lblCurrentFile.Size        = new Size(552, 40);
        lblCurrentFile.ForeColor   = Color.DimGray;
        lblCurrentFile.Text        = string.Empty;

        grpExecution.Controls.AddRange([
            btnStartIndex, btnCancelIndex,
            pbIndexProgress, lblIndexPhase, lblCurrentFile]);
        grpExecution.Location = new Point(8, 6);
        grpExecution.Name     = "grpExecution";
        grpExecution.Size     = new Size(576, 164);
        grpExecution.Text     = "인덱싱 실행";
        grpExecution.TabStop  = false;

        // ── GroupBox: 초기화 (위험 구역) ─────────────────────
        btnResetAll.Location   = new Point(12, 24);
        btnResetAll.Size       = new Size(175, 30);
        btnResetAll.Text       = "🗑  인덱스 전체 초기화";
        btnResetAll.TabIndex   = 2;
        btnResetAll.UseVisualStyleBackColor = true;

        lblResetHint.AutoSize  = true;
        lblResetHint.ForeColor = Color.Gray;
        lblResetHint.Location  = new Point(196, 30);
        lblResetHint.Text      = "파일 목록, 내용 인덱스, 검색 히스토리를 모두 삭제합니다";

        grpDanger.Controls.AddRange([btnResetAll, lblResetHint]);
        grpDanger.ForeColor = Color.Firebrick;
        grpDanger.Location  = new Point(8, 178);
        grpDanger.Name      = "grpDanger";
        grpDanger.Size      = new Size(576, 68);
        grpDanger.Text      = "초기화 (위험)";
        grpDanger.TabStop   = false;

        // ── GroupBox: 인덱스 통계 ─────────────────────────────
        lblStatTotal.AutoSize      = true;
        lblStatTotal.Location      = new Point(12, 24);
        lblStatTotal.Text          = "총 파일 수:           —";

        lblStatContent.AutoSize    = true;
        lblStatContent.Location    = new Point(12, 44);
        lblStatContent.Text        = "내용 인덱싱 파일 수:  —";

        lblStatDbSize.AutoSize     = true;
        lblStatDbSize.Location     = new Point(12, 64);
        lblStatDbSize.Text         = "DB 크기:              —";

        lblStatLastUpdate.AutoSize = true;
        lblStatLastUpdate.Location = new Point(12, 84);
        lblStatLastUpdate.Text     = "마지막 업데이트:      —";

        grpStats.Controls.AddRange([
            lblStatTotal, lblStatContent, lblStatDbSize, lblStatLastUpdate]);
        grpStats.Location = new Point(8, 255);
        grpStats.Name     = "grpStats";
        grpStats.Size     = new Size(576, 118);
        grpStats.Text     = "인덱스 통계";
        grpStats.TabStop  = false;

        tabRun.Controls.AddRange([grpExecution, grpDanger, grpStats]);
        tabRun.Name    = "tabRun";
        tabRun.Padding = new Padding(4);
        tabRun.Text    = "인덱싱 실행";
        tabRun.UseVisualStyleBackColor = true;

        // ═════════════════════════════════════════════════════
        // TabControl
        // ═════════════════════════════════════════════════════
        tabCtrl.Anchor      = AnchorStyles.Top | AnchorStyles.Bottom
                            | AnchorStyles.Left | AnchorStyles.Right;
        tabCtrl.Controls.AddRange([tabPaths, tabSearch, tabRun]);
        tabCtrl.Location    = new Point(8, 8);
        tabCtrl.Name        = "tabCtrl";
        tabCtrl.SelectedIndex = 0;
        tabCtrl.Size        = new Size(599, 460);
        tabCtrl.TabIndex    = 0;

        // ═════════════════════════════════════════════════════
        // 저장 / 취소 버튼 (폼 하단)
        // ═════════════════════════════════════════════════════
        btnSave.Anchor   = AnchorStyles.Bottom | AnchorStyles.Right;
        btnSave.Location = new Point(518, 477);
        btnSave.Size     = new Size(88, 30);
        btnSave.TabIndex = 98;
        btnSave.Text     = "저장";
        btnSave.UseVisualStyleBackColor = true;

        btnCancel.Anchor      = AnchorStyles.Bottom | AnchorStyles.Right;
        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Location    = new Point(424, 477);
        btnCancel.Size        = new Size(88, 30);
        btnCancel.TabIndex    = 99;
        btnCancel.Text        = "취소";
        btnCancel.UseVisualStyleBackColor = true;

        // ═════════════════════════════════════════════════════
        // Form
        // ═════════════════════════════════════════════════════
        AcceptButton        = btnSave;
        CancelButton        = btnCancel;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        ClientSize          = new Size(615, 517);
        Controls.AddRange([tabCtrl, btnSave, btnCancel]);
        Font                = new Font("Segoe UI", 9F);
        FormBorderStyle     = FormBorderStyle.Sizable;
        MinimumSize         = new Size(570, 480);
        Name                = "IndexSettingsForm";
        StartPosition       = FormStartPosition.CenterParent;
        Text                = "인덱싱 설정";

        // ── ResumeLayout ─────────────────────────────────────
        grpStats.ResumeLayout(false);
        grpStats.PerformLayout();
        grpDanger.ResumeLayout(false);
        grpDanger.PerformLayout();
        grpExecution.ResumeLayout(false);
        grpExecution.PerformLayout();
        tabRun.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)nudMaxResults).EndInit();
        grpSearchOpts.ResumeLayout(false);
        grpSearchOpts.PerformLayout();
        grpContent.ResumeLayout(false);
        grpContent.PerformLayout();
        tabSearch.ResumeLayout(false);
        tabPaths.ResumeLayout(false);
        tabPaths.PerformLayout();
        tabCtrl.ResumeLayout(false);
        ResumeLayout(false);
    }

    // ── 컨트롤 선언 ──────────────────────────────────────────
    private TabControl   tabCtrl         = null!;
    private TabPage      tabPaths        = null!;
    private TabPage      tabSearch       = null!;
    private TabPage      tabRun          = null!;
    private Button       btnSave         = null!;
    private Button       btnCancel       = null!;

    // Tab 1
    private Label        lblPathsHint    = null!;
    private ListBox      lstPaths        = null!;
    private Button       btnAddPath      = null!;
    private Button       btnRemovePath   = null!;

    // Tab 2
    private GroupBox     grpContent          = null!;
    private CheckBox     chkEnableContent    = null!;
    private Label        lblContentDesc      = null!;
    private Label        lblReindexWarning   = null!;
    private Label        lblTotalFiles       = null!;
    private Label        lblContentIndexed   = null!;
    private ProgressBar  pbContentRatio      = null!;
    private Button       btnRunContent       = null!;
    private Button       btnClearContent     = null!;
    private GroupBox     grpSearchOpts       = null!;
    private Label        lblMaxResults       = null!;
    private NumericUpDown nudMaxResults      = null!;
    private CheckBox     chkShowSnippet      = null!;

    // Tab 3
    private GroupBox     grpExecution        = null!;
    private Button       btnStartIndex       = null!;
    private Button       btnCancelIndex      = null!;
    private ProgressBar  pbIndexProgress     = null!;
    private Label        lblIndexPhase       = null!;
    private Label        lblCurrentFile      = null!;
    private GroupBox     grpDanger           = null!;
    private Button       btnResetAll         = null!;
    private Label        lblResetHint        = null!;
    private GroupBox     grpStats            = null!;
    private Label        lblStatTotal        = null!;
    private Label        lblStatContent      = null!;
    private Label        lblStatDbSize       = null!;
    private Label        lblStatLastUpdate   = null!;
}
