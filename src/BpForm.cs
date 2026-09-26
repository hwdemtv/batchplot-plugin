// BpForm.cs — BPLOT 出图对话框（③）
// 列表勾选、逐框改比例/文件名、纸张预览、黑白/留白/合成单PDF、输出目录
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace BpPlot
{
    public class BpForm : Form
    {
        readonly List<FrameInfo> _frames;
        readonly List<Media> _medias;
        readonly int _globalScale;
        readonly string _dwgName;

        public List<FrameInfo> Selected;   // OK 后：勾选且已应用行内修改的图框
        public string OutDir;
        public bool Mono = true;
        public bool Merge;
        public new double Margin;          // 打印留白 mm（显式隐藏 Form.Margin）

        DataGridView _grid;
        TextBox _txtDir;
        CheckBox _chkMono, _chkMerge;
        NumericUpDown _numMargin;

        public BpForm(List<FrameInfo> frames, List<Media> medias, int globalScale, string outDir, string dwgName)
        {
            _frames = frames;
            _medias = medias;
            _globalScale = globalScale;
            _dwgName = dwgName;
            OutDir = outDir;
            BuildUi();
            FillGrid(frames);
        }

        void BuildUi()
        {
            Text = "BPPlot 批量出图 v0.6 — " + _dwgName;
            Width = 1000; Height = 640;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;   // 高 DPI 屏不再整体偏小
            Font = new Font("Microsoft YaHei UI", 9F);

            var top = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(8) };
            var lblDir = new Label { Text = "输出目录:", Location = new Point(8, 10), AutoSize = true };
            _txtDir = new TextBox { Location = new Point(75, 7), Width = 700, Text = OutDir ?? "" };
            var btnDir = new Button { Text = "浏览...", Location = new Point(782, 5), Width = 70 };
            btnDir.Click += delegate
            {
                using (var fb = new FolderBrowserDialog { SelectedPath = _txtDir.Text })
                    if (fb.ShowDialog(this) == DialogResult.OK) _txtDir.Text = fb.SelectedPath;
            };
            _chkMono = new CheckBox { Text = "黑白 (monochrome.ctb)", Location = new Point(8, 36), AutoSize = true, Checked = true };
            _chkMerge = new CheckBox { Text = "合成单个 PDF（含图号书签）", Location = new Point(180, 36), AutoSize = true };
            var lblMargin = new Label { Text = "留白 mm(±):", Location = new Point(390, 36), AutoSize = true };
            _numMargin = new NumericUpDown { Location = new Point(478, 33), Width = 70, Minimum = -50, Maximum = 50, DecimalPlaces = 1, Value = 0 };

            top.Controls.AddRange(new Control[] { lblDir, _txtDir, btnDir, _chkMono, _chkMerge, lblMargin, _numMargin });

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
            };
            var cSel = new DataGridViewCheckBoxColumn { HeaderText = "出图", Name = "sel", Width = 46 };
            var cNum = new DataGridViewTextBoxColumn { HeaderText = "图号", Name = "num", ReadOnly = true, FillWeight = 14 };
            var cTitle = new DataGridViewTextBoxColumn { HeaderText = "图名", Name = "title", ReadOnly = true, FillWeight = 24 };
            var cScale = new DataGridViewTextBoxColumn { HeaderText = "比例 1:n", Name = "scale", FillWeight = 10, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } };
            var cName = new DataGridViewTextBoxColumn { HeaderText = "输出文件名(可改)", Name = "fname", FillWeight = 26 };
            var cPaper = new DataGridViewTextBoxColumn { HeaderText = "纸张预览", Name = "paper", ReadOnly = true, FillWeight = 22 };
            _grid.Columns.AddRange(new DataGridViewColumn[] { cSel, cNum, cTitle, cScale, cName, cPaper });
            _grid.CellValueChanged += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.ColumnIndex == _grid.Columns["scale"].Index && e.RowIndex >= 0)
                    UpdatePaper(e.RowIndex);
            };
            // 勾选列单击即切换（EditOnKeystrokeOrF2 下单击默认只选中不勾选，用户易"以为勾上了"）
            int selIdx = _grid.Columns["sel"].Index;
            _grid.CellContentClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.ColumnIndex == selIdx && e.RowIndex >= 0)
                {
                    object v = _grid.Rows[e.RowIndex].Cells[selIdx].Value;
                    _grid.Rows[e.RowIndex].Cells[selIdx].Value = !(v is bool && (bool)v);
                }
            };
            // 留白改变联动纸张预览
            _numMargin.ValueChanged += delegate { RefreshAllPapers(); };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8) };
            var btnAll = new Button { Text = "全选", Location = new Point(8, 8), Width = 60 };
            btnAll.Click += delegate { SetAll(true); };
            var btnNone = new Button { Text = "全不选", Location = new Point(74, 8), Width = 70 };
            btnNone.Click += delegate { SetAll(false); };
            var btnOk = new Button { Text = "出 图", Location = new Point(820, 8), Width = 120, Height = 28, BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White };
            btnOk.Click += delegate( object s, EventArgs e)
            {
                OutDir = _txtDir.Text.Trim();
                if (OutDir.Length == 0) { MessageBox.Show(this, "请填写输出目录"); return; }
                Mono = _chkMono.Checked;
                Merge = _chkMerge.Checked;
                Margin = decimal.ToDouble(_numMargin.Value);
                ApplyAndClose();
            };
            var btnCancel = new Button { Text = "取消", Location = new Point(700, 8), Width = 60 };
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            bottom.Controls.AddRange(new Control[] { btnAll, btnNone, btnCancel, btnOk });

            Controls.Add(_grid);
            Controls.Add(top);
            Controls.Add(bottom);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        void FillGrid(List<FrameInfo> frames)
        {
            _grid.Rows.Clear();
            foreach (FrameInfo f in frames)
            {
                int sc = f.Scale > 0 ? f.Scale : _globalScale;
                string fname = Cmds.ApplyTemplate(f, sc, _grid.Rows.Count + 1);
                _grid.Rows.Add(true, f.Number ?? "", f.Title ?? "", sc.ToString(), fname, PaperPreview(f, sc));
            }
        }

        // 纸张预览：与 BuildPage 同口径（正留白放大纸张、负留白=布满）
        string PaperPreview(FrameInfo f, int sc)
        {
            double mg = decimal.ToDouble(_numMargin.Value);
            double nw = (f.MaxX - f.MinX) / sc, nh = (f.MaxY - f.MinY) / sc;
            if (mg > 0) { nw += 2 * mg; nh += 2 * mg; }
            int rot; bool ex, cr;
            Media m = Cmds.PickMedia(_medias, nw, nh, out rot, out ex, out cr);
            if (m == null) return "-";
            if (mg < 0) ex = false;
            return PrettyMedia(m.CanonicalName) + (ex ? "" : "(布满)");
        }

        void UpdatePaper(int row)
        {
            try
            {
                FrameInfo probe = _frames[_grid.Rows[row].Index];
                int sc;
                if (!int.TryParse("" + _grid.Rows[row].Cells["scale"].Value, out sc) || sc < 1) return;
                _grid.Rows[row].Cells["paper"].Value = PaperPreview(probe, sc);
            }
            catch { }
        }

        void RefreshAllPapers()
        {
            try
            {
                for (int i = 0; i < _grid.Rows.Count; i++) UpdatePaper(i);
            }
            catch { }
        }

        static string PrettyMedia(string canonical)
        {
            if (canonical == null) return "-";
            string s = canonical;
            int i = s.IndexOf('(');
            if (i >= 0) s = s.Substring(i + 1).Replace("_", " ").Replace("MM)", "mm").Replace("毫米", "mm");
            return s.Replace("  ", " ");
        }

        void SetAll(bool v)
        {
            foreach (DataGridViewRow r in _grid.Rows) r.Cells["sel"].Value = v;
        }

        void ApplyAndClose()
        {
            // 提交正在编辑的单元格：AcceptButton（回车）触发时编辑值尚未写回，直接读会丢最后一次修改
            try { _grid.EndEdit(); _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); } catch { }
            _grid.CurrentCell = null;
            Selected = new List<FrameInfo>();
            foreach (DataGridViewRow r in _grid.Rows)
            {
                if (!(bool)(r.Cells["sel"].Value ?? false)) continue;
                FrameInfo f = _frames[r.Index];
                int sc;
                if (int.TryParse("" + r.Cells["scale"].Value, out sc) && sc >= 1)
                    f.Scale = sc;   // 对话框逐框比例覆盖（0 语义消除）
                f.NameOverride = ("" + r.Cells["fname"].Value).Trim();
                Selected.Add(f);
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
