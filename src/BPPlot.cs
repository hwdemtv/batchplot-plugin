// BPPlot.cs — AutoCAD 批量打印 MVP（原创实现）
// 功能：识别模型空间图框（图块/闭合多段线），按打印比例自动匹配纸张，
//       逐框输出 PDF（DWG To PDF.pc3，精确比例，居中）。
// 命令：BPLOT      交互式：手选图框 → 输入比例 → 输入输出目录
//       BPLOTAUTO  脚本/自动化：自动识别全部图框，比例取 USERS1，输出目录取 USERS2
// 目标平台：AutoCAD 2021~2024（.NET Framework 4.8），本机 AutoCAD 2023 验证
// 无任何硬件指纹采集、无网络访问。
//
// v0.6 修复（2026-09）：
//   1. CropPdf 支持旋转页（rot=90）：竖版图框不再被裁切错位（先按 BPPLOT_KEEP_ORIG=1
//      实测中间 PDF 形态后定稿变换）
//   2. 图框收集只认模型空间实体（OwnerId 校验），布局页签/跨布局选择不再产生幽灵图框
//   3. 比例解析防溢出（TryParse）；非标准比例（1:150/250/500/1000 等）改用
//      SetCustomPrintScale，去掉 1000 → 1000:1 的错误枚举映射
//   4. 未保存图纸默认输出目录回退到"我的文档"；设备探测/建目录异常就地报错
//   5. 负留白与"纸张小于内容"时不再进入裁切阶段（语义与文档一致）
//   6. 加载即出图（USERS2 触发）仅 accoreconsole 生效，GUI 下 NETLOAD 不再误触发
//   7. config.json 换成正规迷你 JSON 解析器（值含逗号/转义不再解析错乱）
//   8. 对话框：提交前 EndEdit；勾选列单击即切换；DPI 自适应；预览含留白
//   9. 出图失败的页 CSV 记 FAIL；合并跳过缺失页并报出真实原因
//  10. 空窗判定自动模式下排除图框自身；剔除包含框有提示；CSV 按序号排序；
//      文件名处理 Windows 保留名与尾部点；纸张统计按真实图幅折算
//  11. 性能：模型空间实体外接框一次性缓存（可见性检查与 BPSPLIT 共用）

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using Autodesk.AutoCAD.Runtime;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;

namespace BpPlot
{
    // 加载入口：脚本模式下（USERS2 已设）加载即出图，绕开控制台命令注册限制
    public class App : Autodesk.AutoCAD.Runtime.IExtensionApplication
    {
        public void Initialize()
        {
            // 图形界面：挂「BP-批量打印」菜单栏（重复 NETLOAD 自动去重）
            bool isConsole = false;
            try
            {
                isConsole = string.Equals(Process.GetCurrentProcess().ProcessName,
                    "accoreconsole", StringComparison.OrdinalIgnoreCase);
            }
            catch { }

            if (!isConsole)
            {
                BpMenu.Install();
                return;
            }

            try
            {
                // 控制台专属："加载即出图"（USERS2 非空触发）。
                // 完整版 acad 的 USERS2 是通用变量，其他 LISP/插件可能正在用，
                // 静默整图批打属于误伤；GUI 下请手动执行 BPLOTAUTO 或用菜单。
                string dir = "";
                object u2 = Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("USERS2");
                if (u2 != null) dir = u2.ToString().Trim();
                if (dir.Length > 0)
                {
                    int scale = 100;
                    object u1 = Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("USERS1");
                    if (u1 != null)
                    {
                        int v;
                        if (int.TryParse(u1.ToString(), out v) && v >= 1) scale = v;
                    }
                    // 防重复：立即清空触发条件
                    try { Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable("USERS2", ""); }
                    catch { }
                    Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                    if (doc != null) new Cmds().Run(doc, true, scale, dir);
                }
            }
            catch (System.Exception ex)
            {
                try
                {
                    Document d = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                    if (d != null) d.Editor.WriteMessage("\nBPPLOT Init 异常: " + ex.Message + "\n");
                }
                catch { }
            }
        }

        public void Terminate() { }
    }

    public class FrameInfo
    {
        public double MinX, MinY, MaxX, MaxY;
        public string Number;     // 图号
        public string Title;      // 图名
        public int Scale;         // 图框内比例分母；0 = 使用全局比例
        public string NameOverride; // 对话框指定的文件名（优先于模板）
        public ObjectId SourceId; // 图框实体（空窗检查时按模式排除自身）
        public bool FromAuto;     // 自动识别的图框（空窗判定排除图框自身；手选图框自身即内容）
        public double AreaX { get { return (MaxX - MinX) * (MaxY - MinY); } }
    }

    // 模型空间实体外接框缓存（可见性检查 / BPSPLIT 共用，避免逐框全量重算 GeometricExtents）
    public class EntBox
    {
        public ObjectId Id;
        public double MinX, MinY, MaxX, MaxY;
    }

    public class Media
    {
        public string CanonicalName;
        public double W, H;
    }

    // ---------- 开放式配置（%APPDATA%\BPPlot\config.json，UTF-8）----------
    public class BPConfig
    {
        public List<string> FrameBlocks = new List<string>();   // 学习到的图框块名（自动识别白名单）
        public List<string> NumberTags = new List<string>();    // 图号属性标签
        public List<string> NameTags = new List<string>();      // 图名属性标签
        public List<string> ScaleTags = new List<string>();     // 比例属性标签
        public List<string> RevTags = new List<string>();       // 版次属性标签
        public List<string> DateTags = new List<string>();      // 日期属性标签
        public string FileTemplate = "{图号}_{图名}";
        public double MinPolylineSide = 15000;   // 自动模式多段线图框最短边（图形单位），过滤小矩形误判；0=关闭

        public bool HasTemplate { get { return FileTemplate != null && FileTemplate.Length > 0; } }

        public static string FilePath()
        {
            string d = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BPPlot");
            Directory.CreateDirectory(d);
            return Path.Combine(d, "config.json");
        }

        public static BPConfig Load()
        {
            BPConfig c = new BPConfig();
            try
            {
                string p = FilePath();
                if (!File.Exists(p)) return c;
                string s = File.ReadAllText(p);
                Dictionary<string, object> map;
                if (!JsonParseObject(s, out map)) return c;   // 解析失败保持默认（坏配置静默回退）
                FillList(map, "frameBlocks", c.FrameBlocks);
                FillList(map, "numberTags", c.NumberTags);
                FillList(map, "nameTags", c.NameTags);
                FillList(map, "scaleTags", c.ScaleTags);
                FillList(map, "revTags", c.RevTags);
                FillList(map, "dateTags", c.DateTags);
                object v;
                if (map.TryGetValue("fileTemplate", out v) && v is string) c.FileTemplate = (string)v;
                if (map.TryGetValue("minPolylineSide", out v) && v is double) c.MinPolylineSide = (double)v;
            }
            catch { }
            return c;
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"frameBlocks\": ").Append(WriteList(FrameBlocks)).Append(",\n");
            sb.Append("  \"numberTags\": ").Append(WriteList(NumberTags)).Append(",\n");
            sb.Append("  \"nameTags\": ").Append(WriteList(NameTags)).Append(",\n");
            sb.Append("  \"scaleTags\": ").Append(WriteList(ScaleTags)).Append(",\n");
            sb.Append("  \"revTags\": ").Append(WriteList(RevTags)).Append(",\n");
            sb.Append("  \"dateTags\": ").Append(WriteList(DateTags)).Append(",\n");
            sb.Append("  \"minPolylineSide\": ").Append(MinPolylineSide.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"fileTemplate\": \"").Append(Escape(FileTemplate)).Append("\"\n}\n");
            File.WriteAllText(FilePath(), sb.ToString(), Encoding.UTF8);
        }

        static void FillList(Dictionary<string, object> map, string key, List<string> target)
        {
            object v;
            if (!map.TryGetValue(key, out v)) return;
            List<string> list = v as List<string>;
            if (list == null) return;
            target.Clear();
            target.AddRange(list);
        }

        // ---------- 迷你 JSON 解析（仅支持本配置子集：对象 / 字符串数组 / 数值 / 字符串） ----------
        // 正规 tokenizer 实现，替代原正则方案：值中含逗号、引号转义、换行均不再解析错乱
        static int JsonSkipWs(string s, int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
            return i;
        }

        static int JsonParseString(string s, int i, out string val)
        {
            val = null;
            i = JsonSkipWs(s, i);
            if (i >= s.Length || s[i] != '"') return -1;
            StringBuilder sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\\')
                {
                    if (i + 1 >= s.Length) return -1;
                    char e = s[i + 1];
                    if (e == '"') sb.Append('"');
                    else if (e == '\\') sb.Append('\\');
                    else if (e == '/') sb.Append('/');
                    else if (e == 'n') sb.Append('\n');
                    else if (e == 't') sb.Append('\t');
                    else if (e == 'r') sb.Append('\r');
                    else if (e == 'b') sb.Append('\b');
                    else if (e == 'f') sb.Append('\f');
                    else if (e == 'u' && i + 5 < s.Length)
                    {
                        int code;
                        if (!int.TryParse(s.Substring(i + 2, 4), NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out code)) return -1;
                        sb.Append((char)code);
                        i += 4;
                    }
                    else return -1;
                    i += 2;
                }
                else if (c == '"') { val = sb.ToString(); return i + 1; }
                else { sb.Append(c); i++; }
            }
            return -1;
        }

        public static bool JsonParseObject(string s, out Dictionary<string, object> map)
        {
            map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            int i = JsonSkipWs(s, 0);
            if (i >= s.Length || s[i] != '{') return false;
            i = JsonSkipWs(s, i + 1);
            if (i < s.Length && s[i] == '}') return true;
            while (true)
            {
                string key;
                int j = JsonParseString(s, i, out key);
                if (j < 0) return false;
                i = JsonSkipWs(s, j);
                if (i >= s.Length || s[i] != ':') return false;
                i = JsonSkipWs(s, i + 1);
                if (i < s.Length && s[i] == '[')
                {
                    List<string> list = new List<string>();
                    i = JsonSkipWs(s, i + 1);
                    while (true)
                    {
                        if (i < s.Length && s[i] == ']') { i++; break; }
                        string item;
                        int k = JsonParseString(s, i, out item);
                        if (k < 0) return false;
                        list.Add(item);
                        i = JsonSkipWs(s, k);
                        if (i < s.Length && s[i] == ',') { i = JsonSkipWs(s, i + 1); continue; }
                        if (i < s.Length && s[i] == ']') { i++; break; }
                        return false;
                    }
                    map[key] = list;
                }
                else if (i < s.Length && s[i] == '"')
                {
                    string v;
                    int k = JsonParseString(s, i, out v);
                    if (k < 0) return false;
                    map[key] = v;
                    i = JsonSkipWs(s, k);
                }
                else
                {
                    int k = i;
                    while (k < s.Length && "-+.0123456789eE".IndexOf(s[k]) >= 0) k++;
                    if (k == i) return false;
                    double d;
                    if (!double.TryParse(s.Substring(i, k - i), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out d)) return false;
                    map[key] = d;
                    i = JsonSkipWs(s, k);
                }
                if (i < s.Length && s[i] == ',') { i = JsonSkipWs(s, i + 1); continue; }
                if (i < s.Length && s[i] == '}') return true;
                return false;
            }
        }

        static string WriteList(List<string> list)
        {
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('"').Append(Escape(list[i])).Append('"');
            }
            sb.Append(']');
            return sb.ToString();
        }

        // JSON 字符串转义：引号/反斜杠/控制字符（原实现漏控制字符，模板含换行会写出坏 JSON）
        static string Escape(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    public class Cmds
    {
        const string DeviceName = "DWG To PDF.pc3";
        const double FitTol = 0.6;        // mm，纸张与图框尺寸的匹配容差
        const int MaxNameLen = 80;        // 输出文件名最大长度

        static BPConfig Cfg = BPConfig.Load();

        // ---------- 交互式入口（对话框） ----------
        [CommandMethod("BPLOT", CommandFlags.Modal)]
        public void BpInteractive()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            List<FrameInfo> frames;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                frames = CollectFrames(ed, db, tr, false);   // 手选任意实体
                tr.Commit();
            }
            if (frames.Count == 0) { ed.WriteMessage("\nBPLOT: 未选择图框\n"); return; }

            // 全局比例取 USERS1（与 BPLOTAUTO/BP1 一致；图框属性比例仍逐框优先）
            int globalScale = 100;
            try
            {
                object u1 = Application.GetSystemVariable("USERS1");
                int v;
                if (u1 != null && int.TryParse(u1.ToString(), out v) && v >= 1) globalScale = v;
            }
            catch { }

            string outDir = "";
            try
            {
                object u2 = Application.GetSystemVariable("USERS2");
                if (u2 != null) outDir = u2.ToString().Trim();
            }
            catch { }
            if (outDir.Length == 0) outDir = DefaultOutDir(db, "PDF_OUT");

            // 探测纸张表（detached 对象，不动当前布局）；设备缺失时明确报错而非异常弹窗
            List<Media> medias;
            try
            {
                PlotSettingsValidator val = PlotSettingsValidator.Current;
                using (PlotSettings probe = new PlotSettings(true))
                {
                    val.SetPlotConfigurationName(probe, DeviceName, null);
                    medias = ParseMediaList(val.GetCanonicalMediaNameList(probe));
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nBPLOT: 打印设备 " + DeviceName + " 不可用: " + ex.Message + "\n");
                return;
            }
            if (medias.Count == 0) { ed.WriteMessage("\nBPLOT: 打印设备无可用毫米纸张\n"); return; }

            BpForm form = new BpForm(frames, medias, globalScale, outDir, Path.GetFileName(db.Filename));
            Application.ShowModalDialog(form);
            if (form.DialogResult != System.Windows.Forms.DialogResult.OK || form.Selected == null
                || form.Selected.Count == 0) return;

            PlotFrames2(doc, form.Selected, globalScale, form.OutDir, form.Mono, form.Margin, form.Merge);
        }

        // ---------- 自动化入口（accoreconsole / 脚本） ----------
        [CommandMethod("BPLOTAUTO", CommandFlags.Modal)]
        public void BpAuto()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            int scale = 100;
            string outDir = "";
            try
            {
                object u1 = Application.GetSystemVariable("USERS1");
                if (u1 != null)
                {
                    int v;
                    if (int.TryParse(u1.ToString(), out v) && v >= 1) scale = v;
                }
                object u2 = Application.GetSystemVariable("USERS2");
                if (u2 != null) outDir = u2.ToString();
            }
            catch { }

            Run(doc, true, scale, outDir);
        }

        // ---------- 图框学习：选一个真实图框块，自动记录块名+属性标签映射 ----------
        [CommandMethod("BPTEACH", CommandFlags.Modal)]
        public void BpTeach()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            PromptEntityOptions peo = new PromptEntityOptions("\n选择一个图框块: ");
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                BlockReference br = tr.GetObject(per.ObjectId, OpenMode.ForRead) as BlockReference;
                if (br == null)
                {
                    ed.WriteMessage("BPTEACH: 选中的不是图块（闭合多段线图框无需学习，自动模式已支持）。\n");
                    return;
                }
                string name = EffectiveName(tr, br);
                List<string> nums = new List<string>(), ttl = new List<string>(), scl = new List<string>(),
                    rev = new List<string>(), dt = new List<string>();
                foreach (ObjectId id in br.AttributeCollection)
                {
                    AttributeReference ar = tr.GetObject(id, OpenMode.ForRead) as AttributeReference;
                    if (ar == null || ar.Tag == null) continue;
                    string tag = ar.Tag;
                    string u = tag.ToUpperInvariant();
                    // 分类判定与 CollectFrames 共用；图号判定排除 TITLE/NAME，避免 DRAWINGTITLE 被归为图号
                    if (IsNumberTag(tag, u)) nums.Add(tag);
                    else if (IsNameTag(tag, u)) ttl.Add(tag);
                    else if (tag.Contains("比例") || u.Contains("SCALE")) scl.Add(tag);
                    else if (tag.Contains("版次") || u.Contains("REV") || u.Contains("VERSION")) rev.Add(tag);
                    else if (tag.Contains("日期") || u.Contains("DATE")) dt.Add(tag);
                }
                BPConfig c = BPConfig.Load();
                if (!ListContainsI(c.FrameBlocks, name))
                {
                    c.FrameBlocks.Add(name);
                    ed.WriteMessage("BPTEACH: 学习图框块 [" + name + "]\n");
                }
                foreach (string t in nums) if (!ListContainsI(c.NumberTags, t)) c.NumberTags.Add(t);
                foreach (string t in ttl) if (!ListContainsI(c.NameTags, t)) c.NameTags.Add(t);
                foreach (string t in scl) if (!ListContainsI(c.ScaleTags, t)) c.ScaleTags.Add(t);
                foreach (string t in rev) if (!ListContainsI(c.RevTags, t)) c.RevTags.Add(t);
                foreach (string t in dt) if (!ListContainsI(c.DateTags, t)) c.DateTags.Add(t);
                c.Save();
                Cfg = BPConfig.Load();
                tr.Commit();
            }
            ed.WriteMessage("BPTEACH: 已保存到 " + BPConfig.FilePath() + "，BPLOTAUTO 将自动识别该类图框。\n");
        }

        // ---------- 批量改图框信息（版次/日期） ----------
        [CommandMethod("BPREV", CommandFlags.Modal)]
        public void BpRev()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            PromptSelectionResult psr = ed.GetSelection();
            if (psr.Status != PromptStatus.OK) return;

            PromptStringOptions ro = new PromptStringOptions("\n版次新值 (直接回车 = 自动 +1): ");
            PromptResult rr = ed.GetString(ro);
            if (rr.Status != PromptStatus.OK) return;
            string revNew = rr.StringResult == null ? "" : rr.StringResult.Trim();

            PromptStringOptions dopt = new PromptStringOptions("\n日期新值 (直接回车 = 今天): ");
            PromptResult dr = ed.GetString(dopt);
            if (dr.Status != PromptStatus.OK) return;
            string dateNew = dr.StringResult == null ? "" : dr.StringResult.Trim();
            if (dateNew.Length == 0) dateNew = DateTime.Now.ToString("yyyy.MM.dd");

            int changed = 0, frames = 0;
            using (DocumentLock dl = doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    BlockReference br = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as BlockReference;
                    if (br == null) continue;
                    frames++;
                    foreach (ObjectId id in br.AttributeCollection)
                    {
                        AttributeReference ar = tr.GetObject(id, OpenMode.ForWrite) as AttributeReference;
                        if (ar == null || ar.Tag == null) continue;
                        string tag = ar.Tag;
                        string u = tag.ToUpperInvariant();
                        bool isRev = TagMatch(Cfg.RevTags, tag) || tag.Contains("版次") || u.Contains("REV");
                        bool isDate = TagMatch(Cfg.DateTags, tag) || tag.Contains("日期") || u.Contains("DATE");
                        if (isRev)
                        {
                            string v = revNew;
                            if (v.Length == 0)
                            {
                                int cur, n;
                                if (int.TryParse((ar.TextString ?? "").Trim(), out cur)) n = cur + 1;
                                else n = 1;
                                v = n.ToString();
                            }
                            ed.WriteMessage("  " + tag + ": " + (ar.TextString ?? "") + " -> " + v + "\n");
                            ar.TextString = v;
                            changed++;
                        }
                        else if (isDate)
                        {
                            ed.WriteMessage("  " + tag + ": " + (ar.TextString ?? "") + " -> " + dateNew + "\n");
                            ar.TextString = dateNew;
                            changed++;
                        }
                    }
                }
                tr.Commit();
            }
            ed.WriteMessage("BPREV: " + frames + " 个图框，更新 " + changed + " 处属性（U 命令可整体撤销）\n");
        }

        // ---------- 单张快打：默认参数，选完即出 ----------
        [CommandMethod("BP1", CommandFlags.Modal)]
        public void BpOne()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            ed.WriteMessage("\nBP1 单张快打：选择图框（任意实体，空格/回车确认）\n");

            int scale = 100;
            string outDir = "";
            try
            {
                object u1 = Application.GetSystemVariable("USERS1");
                int v;
                if (u1 != null && int.TryParse(u1.ToString(), out v) && v >= 1) scale = v;
            }
            catch { }
            try
            {
                object u2 = Application.GetSystemVariable("USERS2");
                if (u2 != null) outDir = u2.ToString();
            }
            catch { }

            Database db = doc.Database;
            List<FrameInfo> frames;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                frames = CollectFrames(ed, db, tr, false);
                tr.Commit();
            }
            if (frames.Count == 0)
            {
                ed.WriteMessage("BP1: 未选择图框\n");
                return;
            }
            PlotFrames(doc, frames, scale, outDir, ReadMono(), ReadMargin());
        }

        // Lisp 探针：验证 LispFunction 在控制台的注册情况
        [LispFunction("BPVER")]
        public object BpVerLisp(ResultBuffer args)
        {
            return new ResultBuffer(new TypedValue((int)LispDataType.Text, "BPPlot v0.7"));
        }

        // ---------- 试验命令：单张打印预览（复用 BuildPage + 预览引擎，路线可行性验证） ----------
        // 选择图框后回车（或直接回车=自动识别取最大图框），弹出该图框的打印预览窗口。
        // 每一步骤写入 %APPDATA%\BPPlot\preview_test.log，供自动化验证与排障。
        [CommandMethod("BPPREVIEW", CommandFlags.Modal)]
        public void BpPreviewTest()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BPPlot", "preview_test.log");
            Action<string> log = delegate(string s)
            {
                string line = DateTime.Now.ToString("HH:mm:ss.fff ") + s;
                try { File.AppendAllText(logPath, line + "\r\n", Encoding.UTF8); } catch { }
                try { ed.WriteMessage("\n" + s); } catch { }
            };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                File.WriteAllText(logPath, "");
            }
            catch { }
            log("BPPREVIEW 开始");

            int scale = 100;
            try
            {
                object u1 = Application.GetSystemVariable("USERS1");
                int v;
                if (u1 != null && int.TryParse(u1.ToString(), out v) && v >= 1) scale = v;
            }
            catch { }

            // 图框来源：手选（回车结束）；未选则自动识别取最大者
            List<FrameInfo> frames;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                frames = CollectFrames(ed, db, tr, false);
                tr.Commit();
            }
            if (frames.Count == 0)
            {
                ed.WriteMessage("\n未手选图框，自动识别...");
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    frames = CollectFrames(ed, db, tr, true);
                    tr.Commit();
                }
                DropContained(frames);
            }
            if (frames.Count == 0) { log("未识别到图框，结束"); return; }
            FrameInfo f = frames[0];
            log(string.Format("图框就绪: {0}x{1} 图形单位（共识别 {2} 框）",
                (f.MaxX - f.MinX).ToString("0"), (f.MaxY - f.MinY).ToString("0"), frames.Count));

            PlotEngine pe = null;
            try
            {
                // 与 BPLOT 相同的参数构建（纸张表/CTB/BuildPage）
                PlotSettingsValidator val = PlotSettingsValidator.Current;
                List<Media> medias;
                using (PlotSettings probe = new PlotSettings(true))
                {
                    val.SetPlotConfigurationName(probe, DeviceName, null);
                    medias = ParseMediaList(val.GetCanonicalMediaNameList(probe));
                }
                if (medias.Count == 0) { log("打印设备无可用毫米纸张"); return; }
                string ctb = FindCtb(val);

                PlotInfo pi;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    ObjectId layId = LayoutManager.Current.GetLayoutId("Model");
                    Layout lay = (Layout)tr.GetObject(layId, OpenMode.ForRead);
                    string mediaUsed; double paperW, paperH, trueW, trueH; int rotUsed;
                    pi = BuildPage(val, lay, f, scale, medias, doc, ctb, 0,
                        out mediaUsed, out paperW, out paperH, out trueW, out trueH, out rotUsed);
                    tr.Commit();
                    log(string.Format("BuildPage OK: 纸张={0} 旋转={1}° 比例=1:{2} 图幅={3}x{4}mm 落纸={5}x{6}mm",
                        mediaUsed, rotUsed, scale, trueW.ToString("0.#"), trueH.ToString("0.#"),
                        paperW.ToString("0.#"), paperH.ToString("0.#")));
                }

                log("CreatePreviewEngine((int)PreviewEngineFlags.Plot)...");
                pe = PlotFactory.CreatePreviewEngine((int)PreviewEngineFlags.Plot);
                log("预览引擎创建成功");

                pe.BeginPlot(null, null);
                pe.BeginDocument(pi, Path.GetFileName(doc.Name), null, 1, false, null);
                pe.BeginPage(new PlotPageInfo(), pi, true, null);
                pe.BeginGenerateGraphics(null);
                pe.EndGenerateGraphics(null);
                pe.EndPage(null);
                log("页面渲染完成（预览窗口应已弹出，关闭后继续）");
                pe.EndDocument(null);
                pe.EndPlot(null);
                log("== 预览流程全部成功 ==");
            }
            catch (System.Exception ex)
            {
                log("预览失败 @ " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // ---------- 交互式批量出图并合成单 PDF ----------
        [CommandMethod("BPLOTMERGE", CommandFlags.Modal)]
        public void BpMerge()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            int scale;
            string outDir;
            if (!PromptScaleDir(ed, out scale, out outDir)) return;

            Database db = doc.Database;
            List<FrameInfo> frames;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                frames = CollectFrames(ed, db, tr, false);
                tr.Commit();
            }
            if (frames.Count == 0) { ed.WriteMessage("BPLOTMERGE: 未选择图框\n"); return; }
            PlotFrames2(doc, frames, scale, outDir, ReadMono(), ReadMargin(), true);
        }

        bool PromptScaleDir(Editor ed, out int scale, out string outDir)
        {
            scale = 100; outDir = "";
            PromptIntegerOptions po = new PromptIntegerOptions("\n输入打印比例分母 (1:n 的 n): ");
            po.DefaultValue = 100;
            po.UseDefaultValue = true;
            po.LowerLimit = 1;
            po.UpperLimit = 100000;
            PromptIntegerResult pir = ed.GetInteger(po);
            if (pir.Status != PromptStatus.OK) return false;
            scale = pir.Value;

            PromptStringOptions so = new PromptStringOptions("\n输出目录 (直接回车 = DWG所在目录\\PDF_OUT): ");
            PromptResult sr = ed.GetString(so);
            if (sr.Status != PromptStatus.OK) return false;
            outDir = sr.StringResult == null ? "" : sr.StringResult.Trim();
            return true;
        }

        bool ReadMono()
        {
            try
            {
                object u3 = Application.GetSystemVariable("USERS3");
                if (u3 != null && u3.ToString().Trim() == "0") return false;
            }
            catch { }
            return true;
        }

        // ---------- 布局批量出图：逐布局用其自身页面设置出 PDF ----------
        [CommandMethod("BPL", CommandFlags.Modal)]
        public void BpLayouts()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            string outDir = "";
            try { object u2 = Application.GetSystemVariable("USERS2"); if (u2 != null) outDir = u2.ToString().Trim(); } catch { }
            if (outDir.Length == 0) outDir = DefaultOutDir(db, "PDF_OUT");
            Directory.CreateDirectory(outDir);

            object oldBg = null;
            try { oldBg = Application.GetSystemVariable("BACKGROUNDPLOT"); Application.SetSystemVariable("BACKGROUNDPLOT", 0); } catch { }

            int ok = 0, fail = 0;
            PlotEngine pe = null;
            try
            {
                pe = PlotFactory.CreatePublishEngine();
                pe.BeginPlot(null, null);
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DBDictionary layDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                    foreach (DBDictionaryEntry de in layDict)
                    {
                        Layout lay = (Layout)tr.GetObject(de.Value, OpenMode.ForRead);
                        if (lay.ModelType) continue;   // 模型空间走 BPLOT
                        try
                        {
                            PlotInfo pi = new PlotInfo();
                            pi.Layout = lay.ObjectId;
                            PlotInfoValidator piv = new PlotInfoValidator();
                            piv.MediaMatchingPolicy = MatchingPolicy.MatchEnabled;
                            piv.Validate(pi);
                            string file = Path.Combine(outDir, Sanitize(lay.LayoutName) + ".pdf");
                            pe.BeginDocument(pi, Path.GetFileName(doc.Name), null, 1, true, file);
                            EmitPage(pe, pi, true);
                            pe.EndDocument(null);
                            ok++;
                            ed.WriteMessage("  [OK] " + lay.LayoutName + " -> " + Path.GetFileName(file) + "\n");
                        }
                        catch (System.Exception ex)
                        {
                            fail++;
                            ed.WriteMessage("  [FAIL] " + lay.LayoutName + ": " + ex.Message + "\n");
                        }
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex) { ed.WriteMessage("BPL 致命错误: " + ex.Message + "\n"); }
            finally { if (pe != null) { try { pe.EndPlot(null); } catch { } } }
            ed.WriteMessage(string.Format("BPL_SUMMARY OK={0} FAIL={1}\n", ok, fail));
            if (oldBg != null) { try { Application.SetSystemVariable("BACKGROUNDPLOT", oldBg); } catch { } }
        }

        // ---------- 按图框拆分 DWG（WBLOCK，每框一个文件） ----------
        [CommandMethod("BPSPLIT", CommandFlags.Modal)]
        public void BpSplit()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            string outDir = "";
            try { object u2 = Application.GetSystemVariable("USERS2"); if (u2 != null) outDir = u2.ToString().Trim(); } catch { }
            if (outDir.Length == 0) outDir = DefaultOutDir(db, "DWG_SPLIT");
            Directory.CreateDirectory(outDir);

            List<FrameInfo> frames;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                frames = CollectFrames(ed, db, tr, false);   // 手选（任意实体）
                tr.Commit();
            }
            if (frames.Count == 0) { ed.WriteMessage("BPSPLIT: 未选择图框\n"); return; }
            int before = frames.Count;
            DropContained(frames);
            if (frames.Count < before)
                ed.WriteMessage("BPSPLIT: 剔除包含/重复图框 " + (before - frames.Count) + " 个，实际 " + frames.Count + " 个\n");

            HashSet<string> usedNames = new HashSet<string>();
            int seq = 0, ok = 0, fail = 0;
            using (DocumentLock dl = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                // 外接框一次性缓存，避免逐框全量扫描实体并重算 GeometricExtents（O(F×N) → O(N + F×命中)）
                List<EntBox> boxes = BuildEntityBoxes(tr, ms);
                foreach (FrameInfo f in frames)
                {
                    seq++;
                    string fname = ApplyTemplate(f, f.Scale > 0 ? f.Scale : 100, seq);
                    string baseName = Sanitize(fname);
                    string uniq = baseName;
                    int k = 2;
                    while (!usedNames.Add(uniq.ToUpperInvariant()))
                        uniq = baseName + "_" + (k++);
                    try
                    {
                        ObjectIdCollection ids = new ObjectIdCollection();
                        foreach (EntBox b in boxes)
                        {
                            if (b.MinX <= f.MaxX && b.MaxX >= f.MinX &&
                                b.MinY <= f.MaxY && b.MaxY >= f.MinY)
                                ids.Add(b.Id);
                        }
                        if (ids.Count == 0) { fail++; ed.WriteMessage("  [FAIL] " + uniq + ": 无实体\n"); continue; }
                        Database newDb = db.Wblock(ids, new Point3d(f.MinX, f.MinY, 0));
                        string file = Path.Combine(outDir, uniq + ".dwg");
                        newDb.SaveAs(file, DwgVersion.Current);
                        newDb.Dispose();
                        ok++;
                        ed.WriteMessage("  [OK] " + uniq + ".dwg (" + ids.Count + " 实体)\n");
                    }
                    catch (System.Exception ex)
                    {
                        fail++;
                        ed.WriteMessage("  [FAIL] " + uniq + ": " + ex.Message + "\n");
                    }
                }
                tr.Commit();
            }
            ed.WriteMessage(string.Format("BPSPLIT_SUMMARY OK={0} FAIL={1}\n", ok, fail));
        }

        // ---------- 主流程 ----------
        public void Run(Document doc, bool autoMode, int scale, string outDir)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (outDir == null || outDir.Length == 0)
                outDir = Path.Combine(Path.GetDirectoryName(db.Filename), "PDF_OUT");

            List<FrameInfo> frames;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                frames = CollectFrames(ed, db, tr, autoMode);
                tr.Commit();
            }

            if (frames.Count == 0)
            {
                ed.WriteMessage("\nBPLOT: 未找到图框（图块名需含 图框/TK/FRAME 或为已学习块名，或多段线为闭合矩形）。\n");
                ed.WriteMessage("BPLOT_SUMMARY OK=0 FAIL=0\n");
                return;
            }

            // 黑白打印默认开（USERS3=0 可关）；留白 USERS4（正=放大纸张保比例，负=固定纸张调比例）
            bool mono = true;
            try
            {
                object u3 = Application.GetSystemVariable("USERS3");
                if (u3 != null && u3.ToString().Trim() == "0") mono = false;
            }
            catch { }

            PlotFrames(doc, frames, scale, outDir, mono, ReadMargin());
        }

        double ReadMargin()
        {
            try
            {
                object u4 = Application.GetSystemVariable("USERS4");
                if (u4 != null)
                {
                    double m;
                    if (double.TryParse(u4.ToString().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out m))
                        return m;
                }
            }
            catch { }
            return 0;
        }

        bool ReadMerge()
        {
            // USERS6 不存在（AutoCAD 只有 USERS1-5），合并开关走环境变量
            return Environment.GetEnvironmentVariable("BPPLOT_MERGE") == "1";
        }

        // ---------- 批量出图核心（BPLOT/BPLOTAUTO/BP1 共用） ----------
        void PlotFrames(Document doc, List<FrameInfo> frames, int scale, string outDir, bool mono, double margin)
        {
            PlotFrames2(doc, frames, scale, outDir, mono, margin, ReadMerge());
        }

        void PlotFrames2(Document doc, List<FrameInfo> frames, int scale, string outDir, bool mono, double margin, bool merge)
        {
            Editor ed = doc.Editor;

            object oldBg = null;
            try
            {
                oldBg = Application.GetSystemVariable("BACKGROUNDPLOT");
                Application.SetSystemVariable("BACKGROUNDPLOT", 0);
            }
            catch { }

            int beforeDedupe = frames.Count;
            DropContained(frames);
            if (frames.Count < beforeDedupe)
                ed.WriteMessage("\nBPLOT: 剔除包含/重复图框 " + (beforeDedupe - frames.Count)
                    + " 个，实际出图 " + frames.Count + " 个\n");
            if (outDir == null || outDir.Trim().Length == 0)
                outDir = DefaultOutDir(doc.Database, "PDF_OUT");
            ed.WriteMessage(string.Format("\nBPLOT v0.7: {0} 个图框，比例 1:{1}{2}{3}{4}，输出 {5}\n",
                frames.Count, scale, mono ? "，黑白" : "，彩色",
                margin != 0 ? "，留白" + margin.ToString("0.##") + "mm" : "",
                merge ? "，合成单PDF" : "", outDir));

            int ok = 0, fail = 0;
            List<string[]> csvRows = new List<string[]>();

            PlotEngine pe = null;
            try
            {
                Directory.CreateDirectory(outDir);
                pe = PlotFactory.CreatePublishEngine();
                pe.BeginPlot(null, null);

                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    ObjectId layId = LayoutManager.Current.GetLayoutId("Model");
                    Layout lay = tr.GetObject(layId, OpenMode.ForWrite) as Layout;
                    if (lay == null)
                        throw new System.Exception("无法打开 Model 布局");

                    PlotSettingsValidator val = PlotSettingsValidator.Current;

                    // 设备 + 可用纸张表（只取一次，用 detached 对象探测）
                    List<Media> medias;
                    using (PlotSettings probe = new PlotSettings(true))
                    {
                        val.SetPlotConfigurationName(probe, DeviceName, null);
                        medias = ParseMediaList(val.GetCanonicalMediaNameList(probe));
                    }
                    if (medias.Count == 0)
                        throw new System.Exception("打印设备 " + DeviceName + " 无可用毫米纸张");

                    string ctb = mono ? FindCtb(val) : null;
                    if (mono)
                        ed.WriteMessage(ctb != null
                            ? "黑白打印样式: " + ctb + "\n"
                            : "黑白打印样式: 未找到 monochrome.ctb，按彩色输出\n");

                    HashSet<string> usedNames = new HashSet<string>();
                    string mergedPath = merge
                        ? Path.Combine(outDir, Sanitize(Path.GetFileNameWithoutExtension(doc.Name)) + "_合并.pdf")
                        : null;
                    _visCache.Clear();

                    // 模型空间实体外接框清单（一次遍历缓存，窗口可见性扫描用）
                    List<EntBox> msBoxes;
                    {
                        BlockTable bt2 = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord ms2 = (BlockTableRecord)tr.GetObject(bt2[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                        msBoxes = BuildEntityBoxes(tr, ms2);
                    }

                    // 阶段一：逐框构建页面设置（跳过窗口内无可见内容的空图框）
                    var pages = new List<object[]>();
                    int seq = 0;
                    foreach (FrameInfo f in frames)
                    {
                        seq++;
                        int fscale = f.Scale > 0 ? f.Scale : scale;
                        string fname = ApplyTemplate(f, fscale, seq);
                        string baseName = Sanitize(fname);
                        string uniq = baseName;
                        int k = 2;
                        while (!usedNames.Add(uniq.ToUpperInvariant()))
                            uniq = baseName + "_" + (k++);

                        if (CountVisibleInWindow(tr, msBoxes, f) == 0)
                        {
                            ed.WriteMessage("  [SKIP] " + fname + "（窗口内无可见内容：图层冻结/关闭或内容为空）\n");
                            csvRows.Add(new string[] { seq.ToString(), "",
                                f.Number ?? "", f.Title ?? "", "1:" + fscale, "", "SKIP(无可见内容)" });
                            continue;
                        }

                        try
                        {
                            if (fscale != scale)
                                ed.WriteMessage("  [note] " + fname + " 使用图框内比例 1:" + fscale + "\n");
                            string mediaUsed;
                            double paperW, paperH, trueW, trueH;
                            int rotUsed;
                            PlotInfo pi = BuildPage(val, lay, f, fscale, medias, doc, ctb, margin,
                                out mediaUsed, out paperW, out paperH, out trueW, out trueH, out rotUsed);
                            string csvName = merge ? ("#第" + (pages.Count + 1) + "页") : (uniq + ".pdf");
                            // 仅当"纸张大于内容超过 0.5mm"才需要裁切：
                            // 负留白语义=固定纸张，纸张小于内容(clipRisk 布满)时也不得裁
                            bool crop = margin >= 0 && trueW > 0 &&
                                (paperW - trueW > 0.5 || paperH - trueH > 0.5);
                            pages.Add(new object[] { pi, uniq, csvName,
                                new string[] { seq.ToString(), csvName, f.Number ?? "", f.Title ?? "",
                                    "1:" + fscale, mediaUsed, "OK" },
                                paperW, paperH, trueW, trueH, crop, rotUsed });
                        }
                        catch (System.Exception ex)
                        {
                            fail++;
                            ed.WriteMessage("  [FAIL] " + fname + ": " + ex.Message + "\n");
                            csvRows.Add(new string[] { seq.ToString(), "", f.Number ?? "", f.Title ?? "",
                                "1:" + fscale, "", "FAIL" });
                        }
                    }

                    // 阶段二：逐框出 PDF（该路径久经验证）
                    foreach (object[] p in pages)
                    {
                        PlotInfo pi = (PlotInfo)p[0];
                        string file = Path.Combine(outDir, (string)p[1] + ".pdf");
                        try
                        {
                            pe.BeginDocument(pi, Path.GetFileName(doc.Name), null, 1, true, file);
                            EmitPage(pe, pi, true);
                            pe.EndDocument(null);
                            ok++;
                            ed.WriteMessage("  [OK] " + (string)p[2] + "\n");
                        }
                        catch (System.Exception ex)
                        {
                            fail++;
                            try { pe.EndDocument(null); } catch { }   // BeginDocument 成功后半途失败时复位引擎
                            ((string[])p[3])[6] = "FAIL";             // CSV 与统计同步，不再虚计 OK
                            ed.WriteMessage("  [FAIL] " + (string)p[2] + ": " + ex.Message + "\n");
                        }
                    }

                    // 阶段三：非标准幅面裁切到真实图幅（① 任意尺寸纸张；rot=90 页做旋转补偿）
                    foreach (object[] p in pages)
                    {
                        if (!(bool)p[8]) continue;
                        string file = Path.Combine(outDir, (string)p[1] + ".pdf");
                        if (CropPdf(file, (double)p[6], (double)p[7], (double)p[4], (double)p[5], (int)p[9]))
                        {
                            csvRowPatch(p, ((double)p[6]).ToString("0.#") + "x" + ((double)p[7]).ToString("0.#") + "mm");
                            ed.WriteMessage("  [crop] " + (string)p[2] + " -> 真实图幅 "
                                + ((double)p[6]).ToString("0.#") + "x" + ((double)p[7]).ToString("0.#") + "mm\n");
                        }
                        else
                        {
                            ed.WriteMessage("  [crop失败] " + (string)p[2] + ": 裁切未生效，保留整幅纸张页"
                                + "（请确认 PdfSharp.dll 与 BPPlot.dll 同目录）\n");
                        }
                    }

                    // 合并模式：PdfSharp 合成单文件（页序=目录顺序，书签=图号图名文件名）
                    if (merge && pages.Count > 0)
                    {
                        var todo = new List<object[]>();
                        foreach (object[] p in pages)
                            todo.Add(new object[] { Path.Combine(outDir, (string)p[1] + ".pdf"), (string)p[1] });
                        string mergeErr;
                        if (MergePdfs(todo, mergedPath, Path.GetFileNameWithoutExtension(doc.Name), out mergeErr))
                            ed.WriteMessage("合并 PDF(" + todo.Count + "页,含书签): " + mergedPath + "\n");
                        else
                            ed.WriteMessage("PDF 合并失败: " + mergeErr
                                + "（保留逐页文件；若提示找不到 PdfSharp 请确认其与 BPPlot.dll 同目录）\n");
                    }
                    foreach (object[] p in pages) csvRows.Add((string[])p[3]);
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("BPLOT 致命错误: " + ex.Message + "\n");
            }
            finally
            {
                if (pe != null)
                {
                    try { pe.EndPlot(null); } catch { }
                }
            }

            WriteCsvAndStats(outDir, csvRows, ed);
            ed.WriteMessage(string.Format("\nBPLOT_SUMMARY OK={0} FAIL={1}\n", ok, fail));

            if (oldBg != null)
            {
                try { Application.SetSystemVariable("BACKGROUNDPLOT", oldBg); } catch { }
            }
        }

        // 文件名规则模板：{图号}{图名}{比例}{日期}{序号}；对话框可逐框指定 NameOverride
        public static string ApplyTemplate(FrameInfo f, int scale, int seq)
        {
            if (f.NameOverride != null && f.NameOverride.Trim().Length > 0)
                return f.NameOverride.Trim();

            bool hasInfo = (f.Number != null && f.Number.Length > 0) || (f.Title != null && f.Title.Length > 0);
            if (!hasInfo) return "Sheet" + seq.ToString("000");

            string t = Cfg.HasTemplate ? Cfg.FileTemplate : "{图号}_{图名}";
            string r = t.Replace("{图号}", f.Number == null ? "" : f.Number)
                        .Replace("{图名}", f.Title == null ? "" : f.Title)
                        .Replace("{比例}", "1:" + scale)
                        .Replace("{日期}", DateTime.Now.ToString("yyyyMMdd"))
                        .Replace("{序号}", seq.ToString("000"));
            while (r.Contains("__")) r = r.Replace("__", "_");
            r = r.Trim('_', '-', '.', ' ');
            if (r.Length == 0)
            {
                if (f.Number != null && f.Number.Length > 0) r = f.Number;
                else r = f.Title;
            }
            return r;
        }

        // 图纸目录 CSV + 纸张统计（A 系折算 A4 当量；按序号排序输出）
        void WriteCsvAndStats(string outDir, List<string[]> rows, Editor ed)
        {
            try
            {
                // 行按序号排序（SKIP 行与 OK 行原本分组乱序，阅读困难）
                List<string[]> ordered = new List<string[]>(rows);
                ordered.Sort(delegate(string[] a, string[] b)
                {
                    int ia, ib;
                    int.TryParse(a[0], out ia);
                    int.TryParse(b[0], out ib);
                    return ia.CompareTo(ib);
                });
                StringBuilder sb = new StringBuilder();
                sb.Append("序号,文件名,图号,图名,比例,纸张,结果\r\n");
                Dictionary<string, int> cnt = new Dictionary<string, int>();
                foreach (string[] r in ordered)
                {
                    sb.Append(r[0]).Append(',').Append(Csv(r[1])).Append(',')
                      .Append(Csv(r[2])).Append(',').Append(Csv(r[3])).Append(',')
                      .Append(r[4]).Append(',').Append(Csv(r[5])).Append(',').Append(r[6]).Append("\r\n");
                    if (r[6] == "OK")
                    {
                        // 裁切过的页优先按真实图幅（"->WxHmm"后缀）折算，避免按整幅原纸高估
                        string std = null;
                        int arrow = r[5].IndexOf("->");
                        if (arrow >= 0)
                        {
                            Match tm = Regex.Match(r[5].Substring(arrow + 2), @"^(\d+(?:\.\d+)?)x(\d+(?:\.\d+)?)mm$");
                            if (tm.Success)
                                std = FoldStandard(double.Parse(tm.Groups[1].Value, CultureInfo.InvariantCulture),
                                                   double.Parse(tm.Groups[2].Value, CultureInfo.InvariantCulture));
                        }
                        if (std == null) std = FoldStandard(r[5]);
                        if (std != null)
                        {
                            if (!cnt.ContainsKey(std)) cnt[std] = 1;
                            else cnt[std]++;
                        }
                    }
                }
                Dictionary<string, int> units = new Dictionary<string, int>();
                units["A0"] = 16; units["A1"] = 8; units["A2"] = 4; units["A3"] = 2; units["A4"] = 1;
                int a4 = 0;
                List<string> parts = new List<string>();
                List<string> order = new List<string> { "A0", "A1", "A2", "A3", "A4" };
                foreach (string key in order)
                {
                    if (!cnt.ContainsKey(key)) continue;
                    parts.Add(key + "x" + cnt[key]);
                    int u;
                    if (units.TryGetValue(key, out u)) a4 += u * cnt[key];
                }
                if (a4 > 0)
                {
                    sb.Append("\r\n纸张统计:,").Append(string.Join("；", parts.ToArray()))
                      .Append(",A4当量合计:,").Append(a4).Append("\r\n");
                    ed.WriteMessage("纸张统计: " + string.Join(" ", parts.ToArray()) + "（A4当量 " + a4 + " 张）\n");
                }
                string csvPath = Path.Combine(outDir, "图纸目录.csv");
                File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
                ed.WriteMessage("目录已导出: " + csvPath + "\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("CSV 导出失败: " + ex.Message + "\n");
            }
        }

        string Csv(string s) { return "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; }

        // 按纸张尺寸折算到 ISO A 系（取能容纳的最小标准，横竖均认，用于统计）
        string FoldStandard(string canonical)
        {
            if (canonical == null) return null;
            Match m = MediaRx.Match(canonical);
            if (!m.Success) return null;
            double w = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double h = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return FoldStandard(w, h);
        }

        // 尺寸重载：容差 3mm（加长幅/裁切后真实图幅常比名义 A 系大零点几毫米，1mm 容差会高估一档）
        string FoldStandard(double w, double h)
        {
            double[,] std = new double[,] { { 297, 210 }, { 420, 297 }, { 594, 420 }, { 841, 594 }, { 1189, 841 } };
            string[] names = new string[] { "A4", "A3", "A2", "A1", "A0" };
            double tol = 3.0;
            for (int i = 0; i < std.GetLength(0); i++)
                if ((w <= std[i, 0] + tol && h <= std[i, 1] + tol) || (w <= std[i, 1] + tol && h <= std[i, 0] + tol))
                    return names[i];
            return "A0";
        }

        // ---------- 选纸（静态，对话框预览共用） ----------
        // 能容纳的最小毫米纸张（含旋转判断），精确匹配时优待 full_bleed（无边距）
        // exact=false 时取最大纸张布满；clipRisk=纸张比图框略小需布满防裁切
        public static Media PickMedia(List<Media> medias, double nw, double nh, out int rot, out bool exact, out bool clipRisk)
        {
            Media best = null;
            int bestRot = 0;
            double bestArea = double.MaxValue;
            double tol = FitTol;
            foreach (Media m in medias)
            {
                if (m.W >= nw - tol && m.H >= nh - tol && m.W * m.H <= bestArea + 0.01)
                {
                    bool better = m.W * m.H < bestArea - 0.01 ||
                        (best == null || (!best.CanonicalName.Contains("full_bleed") && m.CanonicalName.Contains("full_bleed")));
                    if (better) { best = m; bestArea = m.W * m.H; bestRot = 0; }
                }
                if (m.W >= nh - tol && m.H >= nw - tol && m.W * m.H <= bestArea + 0.01)
                {
                    bool better = m.W * m.H < bestArea - 0.01 ||
                        (best == null || (!best.CanonicalName.Contains("full_bleed") && m.CanonicalName.Contains("full_bleed")));
                    if (better) { best = m; bestArea = m.W * m.H; bestRot = 90; }
                }
            }
            rot = bestRot;
            exact = best != null;
            clipRisk = false;
            if (!exact)
            {
                double maxA = 0;
                foreach (Media m in medias)
                    if (m.W * m.H > maxA) { maxA = m.W * m.H; best = m; }
                rot = 0;
            }
            else if (rot == 90
                ? (best.H < nw - 0.01 || best.W < nh - 0.01)
                : (best.W < nw - 0.01 || best.H < nh - 0.01))
            {
                exact = false;
                clipRisk = true;
            }
            return best;
        }

        // ---------- 单页设置构建（选纸+窗口+比例+校验；不含引擎提交） ----------
        PlotInfo BuildPage(PlotSettingsValidator val,
            Layout lay, FrameInfo f, int scale, List<Media> medias, Document doc, string ctb, double margin,
            out string mediaUsed, out double paperW, out double paperH, out double trueW, out double trueH,
            out int rotUsed)
        {
            string step = "start";
            mediaUsed = "";
            paperW = paperH = trueW = trueH = 0;
            rotUsed = 0;
            try
            {
            double nw = (f.MaxX - f.MinX) / scale;
            double nh = (f.MaxY - f.MinY) / scale;
            double wMinX = f.MinX, wMinY = f.MinY, wMaxX = f.MaxX, wMaxY = f.MaxY;
            if (margin < 0)
            {
                // 负留白：图面向外扩，比例微调（纸张按扩后尺寸选）
                double ring = (-margin) * scale;
                wMinX -= ring; wMinY -= ring; wMaxX += ring; wMaxY += ring;
                nw = (wMaxX - wMinX) / scale;
                nh = (wMaxY - wMinY) / scale;
            }
            else if (margin > 0)
            {
                // 正留白：放大纸张保持比例
                nw += 2 * margin;
                nh += 2 * margin;
            }

            int rot;
            bool exact, clipRisk;
            Media best = PickMedia(medias, nw, nh, out rot, out exact, out clipRisk);
            if (clipRisk)
                doc.Editor.WriteMessage("  [note] 图框略大于所选纸张(" + best.CanonicalName + ")，改按布满防裁切\n");
            else if (!exact)
                doc.Editor.WriteMessage("  [note] 图框超出最大纸张(" + best.CanonicalName + ")，按布满缩小输出\n");
            if (margin < 0) exact = false;   // 负留白语义=固定纸张调比例

            paperW = best.W; paperH = best.H; trueW = nw; trueH = nh;
            rotUsed = rot;

            // detached 覆盖设置（官方窗口打印范式），Validate 的匹配不会覆盖显式媒体
            step = "new PlotSettings";
            PlotSettings po = new PlotSettings(lay.ModelType);
            step = "SetPlotConfigurationName";
            val.SetPlotConfigurationName(po, DeviceName, null);
            step = "SetCanonicalMediaName(" + best.CanonicalName + ")";
            val.SetCanonicalMediaName(po, best.CanonicalName);
            step = "SetPlotWindowArea";
            val.SetPlotWindowArea(po, new Extents2d(wMinX, wMinY, wMaxX, wMaxY));
            step = "SetPlotType";
            val.SetPlotType(po, Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
            step = "SetPlotOrigin";
            val.SetPlotOrigin(po, new Point2d(0, 0));
            step = "SetPlotRotation";
            val.SetPlotRotation(po, rot == 90 ? PlotRotation.Degrees090 : PlotRotation.Degrees000);
            step = "SetPlotCentered";
            val.SetPlotCentered(po, true);
            step = "SetPlotPaperUnits";
            val.SetPlotPaperUnits(po, PlotPaperUnit.Millimeters);
            if (exact)
            {
                StdScaleType sst;
                if (TryStdScaleType(scale, out sst))
                {
                    step = "SetUseStandardScale";
                    val.SetUseStandardScale(po, true);
                    step = "SetStdScaleType";
                    val.SetStdScaleType(po, sst);
                    step = "SetStdScale";
                    val.SetStdScale(po, 1.0 / scale);
                }
                else
                {
                    // 非标准比例（1:150/250/500/1000 等）：走自定义比例，
                    // 不再借道 StdScale1To1/1000To1 枚举（原 1000 映射方向相反，靠数值纠正，存在风险）
                    step = "SetCustomPrintScale";
                    val.SetUseStandardScale(po, false);
                    val.SetCustomPrintScale(po, new CustomScale(1.0, scale));   // 1 纸mm : n 图mm
                }
            }
            else
            {
                step = "SetUseStandardScale(fit)";
                val.SetUseStandardScale(po, false);   // 布满纸张
                step = "SetStdScaleType(fit)";
                val.SetStdScaleType(po, StdScaleType.ScaleToFit);
            }
            step = "misc-props";
            po.PlotHidden = false;
            po.PlotPlotStyles = true;
            po.PrintLineweights = true;

            if (ctb != null)
            {
                step = "StyleSheet(" + ctb + ")";
                val.SetCurrentStyleSheet(po, ctb);
            }

            step = "PlotInfo";
            PlotInfo pi = new PlotInfo();
            pi.Layout = lay.ObjectId;
            pi.OverrideSettings = po;
            step = "Validate";
            PlotInfoValidator piv = new PlotInfoValidator();
            piv.MediaMatchingPolicy = MatchingPolicy.MatchEnabled;
            piv.Validate(pi);
            mediaUsed = best.CanonicalName;
            return pi;
            }
            catch (System.Exception ex)
            {
                throw new System.Exception("[" + step + "] " + ex.Message, ex);
            }
        }

        // 引擎提交一页（lastPage：单文件多页时仅末页为 true）
        void EmitPage(PlotEngine pe, PlotInfo pi, bool lastPage)
        {
            pe.BeginPage(new PlotPageInfo(), pi, lastPage, null);
            pe.BeginGenerateGraphics(null);
            pe.EndGenerateGraphics(null);
            pe.EndPage(null);
        }

        static void csvRowPatch(object[] p, string paperSuffix)
        {
            string[] row = (string[])p[3];
            row[5] = row[5] + "->" + paperSuffix;
        }

        // PdfSharp 裁切：把打印页（内容精确比例居中）裁成真实图幅 —— ① 任意尺寸纸张的实现
        // rot：该页打印旋转角（0/90）。设置环境变量 BPPLOT_KEEP_ORIG=1 可保留裁切前的
        // 中间 PDF（*.orig.pdf），用于核对打印引擎在旋转页上的实际页面/内容形态
        static bool CropPdf(string file, double trueW, double trueH, double paperW, double paperH, int rot)
        {
            try
            {
                if (Environment.GetEnvironmentVariable("BPPLOT_KEEP_ORIG") == "1")
                {
                    try { File.Copy(file, file + ".orig.pdf", true); } catch { }
                }
                double pt = 72.0 / 25.4;   // mm → point
                PdfDocument doc = new PdfDocument();
                PdfPage page = doc.AddPage();
                page.Width = XUnit.FromMillimeter(trueW);
                page.Height = XUnit.FromMillimeter(trueH);
                XPdfForm form = XPdfForm.FromFile(file);
                XGraphics g = XGraphics.FromPdfPage(page);
                if (rot == 90)
                {
                    // 旋转页（accoreconsole 实测标定）：打印引擎在名义纸（paperW×paperH）上把
                    // 图面按 90° 转置居中放置——图面 X 轴 → 纸面 Y 轴(向下)，图面 Y 轴 → 纸面 X 轴(向右)，
                    // 占位为 trueH(宽) × trueW(高)。此处反转置回正：图面 (u,v) → 页面 (u, trueH−v)，
                    // 即先 RotateTransform(-90) 再平移。文字方向与位置已按实测校验（误差 <1pt）
                    double fx = (paperW - trueH) / 2.0 * pt;   // 占位区左上角（纸面坐标）
                    double fy = (paperH - trueW) / 2.0 * pt;
                    g.TranslateTransform(-fy, trueH * pt + fx);
                    g.RotateTransform(-90);
                    g.DrawImage(form, new XRect(0, 0, paperW * pt, paperH * pt));
                }
                else
                {
                    // 内容在原纸上居中：反向平移半个差值，MediaBox 裁掉余量（矢量无损）
                    double dx = -(paperW - trueW) / 2.0 * pt;
                    double dy = -(paperH - trueH) / 2.0 * pt;
                    g.DrawImage(form, new XRect(dx, dy, paperW * pt, paperH * pt));
                }
                g.Dispose();
                string tmp = file + ".crop.pdf";
                doc.Save(tmp);
                doc.Close();
                File.Delete(file);
                File.Move(tmp, file);
                return true;
            }
            catch { return false; }
        }

        // PdfSharp 合并逐页 PDF 为单文件，并按图号图名加书签；单页缺失只跳过不整体失败
        bool MergePdfs(List<object[]> todo, string mergedPath, string docTitle, out string error)
        {
            error = null;
            PdfDocument outDoc = null;
            List<PdfDocument> opened = new List<PdfDocument>();
            try
            {
                outDoc = new PdfDocument();
                outDoc.Info.Title = docTitle;
                outDoc.Info.Author = "BPPlot";
                foreach (object[] t in todo)
                {
                    string f = (string)t[0];
                    if (!File.Exists(f))
                    {
                        error = "缺页 " + (string)t[1] + ".pdf（该页可能出图失败），已跳过";
                        continue;
                    }
                    PdfDocument src = PdfReader.Open(f, PdfDocumentOpenMode.Import);
                    opened.Add(src);
                    PdfPage pg = outDoc.AddPage(src.Pages[0]);
                    outDoc.Outlines.Add((string)t[1], pg, true);
                }
                if (outDoc.PageCount == 0) { error = error ?? "无可用页面"; return false; }
                outDoc.Save(mergedPath);
                return true;
            }
            catch (System.Exception ex)
            {
                error = (error == null ? "" : error + "；") + ex.Message;
                return false;
            }
            finally
            {
                foreach (PdfDocument d in opened) { try { d.Close(); } catch { } }
                if (outDoc != null) { try { outDoc.Close(); } catch { } }
            }
        }

        // 实体所在图层是否可见（未冻结且未关闭）
        static bool LayerVisible(Transaction tr, Entity ent)
        {
            try
            {
                LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(ent.LayerId, OpenMode.ForRead);
                return !ltr.IsFrozen && !ltr.IsOff;
            }
            catch { return true; }
        }

        static Dictionary<ObjectId, bool> _visCache = new Dictionary<ObjectId, bool>();

        // 实体是否真实可见：图层可见；块引用递归检查内部图形是否至少有一个可见元素
        bool IsActuallyVisible(Transaction tr, Entity ent)
        {
            try
            {
                if (!LayerVisible(tr, ent)) return false;
                BlockReference br = ent as BlockReference;
                if (br == null) return true;
                bool cached;
                if (_visCache.TryGetValue(br.BlockTableRecord, out cached)) return cached;
                _visCache[br.BlockTableRecord] = true;   // 防循环引用
                bool vis = false;
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    Entity e2 = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (e2 == null || e2 is AttributeDefinition) continue;
                    Extents3d ex; try { ex = e2.GeometricExtents; } catch { continue; }
                    if (IsActuallyVisible(tr, e2)) { vis = true; break; }
                }
                _visCache[br.BlockTableRecord] = vis;
                return vis;
            }
            catch { return true; }
        }

        // 模型空间实体外接框清单（一次遍历；可见性检查/BPSPLIT 复用，避免 O(图框×实体) 反复取 extents）
        static List<EntBox> BuildEntityBoxes(Transaction tr, BlockTableRecord ms)
        {
            List<EntBox> list = new List<EntBox>();
            foreach (ObjectId id in ms)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                Extents3d ex;
                try { ex = ent.GeometricExtents; } catch { continue; }
                EntBox b = new EntBox();
                b.Id = id;
                b.MinX = ex.MinPoint.X; b.MinY = ex.MinPoint.Y;
                b.MaxX = ex.MaxPoint.X; b.MaxY = ex.MaxPoint.Y;
                list.Add(b);
            }
            return list;
        }

        // 窗口内可见实体计数（≥1 即有内容；0 = 空窗跳过）
        // 自动识别的图框排除图框实体自身（否则可见的空图框永远算"有内容"，
        // 会输出只有标题栏的近空页）；手选图框不排除（用户选它就是要打它）
        int CountVisibleInWindow(Transaction tr, List<EntBox> boxes, FrameInfo f)
        {
            foreach (EntBox b in boxes)
            {
                if (f.FromAuto && b.Id == f.SourceId) continue;
                if (b.MinX > f.MaxX || b.MaxX < f.MinX ||
                    b.MinY > f.MaxY || b.MaxY < f.MinY) continue;
                Entity ent = tr.GetObject(b.Id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                if (IsActuallyVisible(tr, ent)) return 1;
            }
            return 0;
        }

        // 标准比例分母 → StdScaleType 枚举；返回 false = 无标准枚举（1:150/250/500/1000 等，
        // 由 BuildPage 走 SetCustomPrintScale 自定义比例。原 1000 → StdScale1000To1 映射方向相反，已移除）
        static bool TryStdScaleType(int n, out StdScaleType t)
        {
            switch (n)
            {
                case 1: t = StdScaleType.StdScale1To1; return true;
                case 2: t = StdScaleType.StdScale1To2; return true;
                case 4: t = StdScaleType.StdScale1To4; return true;
                case 5: t = StdScaleType.StdScale1To5; return true;
                case 8: t = StdScaleType.StdScale1To8; return true;
                case 10: t = StdScaleType.StdScale1To10; return true;
                case 16: t = StdScaleType.StdScale1To16; return true;
                case 20: t = StdScaleType.StdScale1To20; return true;
                case 30: t = StdScaleType.StdScale1To30; return true;
                case 40: t = StdScaleType.StdScale1To40; return true;
                case 50: t = StdScaleType.StdScale1To50; return true;
                case 100: t = StdScaleType.StdScale1To100; return true;
                default: t = StdScaleType.StdScale1To1; return false;
            }
        }

        // ---------- 图框收集 ----------
        List<FrameInfo> CollectFrames(Editor ed, Database db, Transaction tr, bool autoMode)
        {
            List<FrameInfo> frames = new List<FrameInfo>();
            // 只认模型空间实体：SelectAll/GetSelection 会跨布局拿到图纸空间实体，
            // 其外接框是纸面坐标，当成模型窗口打印会产出错位/空白页
            BlockTable bt0 = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            ObjectId msId = bt0[BlockTableRecord.ModelSpace];

            PromptSelectionResult res;
            if (autoMode)
            {
                SelectionFilter filter = new SelectionFilter(new TypedValue[] {
                    new TypedValue((int)DxfCode.Start, "INSERT,LWPOLYLINE")
                });
                res = ed.SelectAll(filter);
            }
            else
            {
                res = ed.GetSelection();   // 手选：任意实体，取其外接框为图幅
            }
            if (res.Status != PromptStatus.OK) return frames;

            foreach (SelectedObject so in res.Value)
            {
                if (so == null) continue;
                Entity ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                if (ent.OwnerId != msId) continue;   // 跳过图纸空间实体

                string fnum = null, ftitle = null;
                int frameScale = 0;
                BlockReference br = ent as BlockReference;
                if (br != null)
                {
                    string bn = EffectiveName(tr, br);
                    if (autoMode && !IsFrameName(bn) && !ListContainsI(Cfg.FrameBlocks, bn)) continue;
                    fnum = AttrByTags(tr, br, Cfg.NumberTags);
                    if (fnum == null)
                        fnum = AttrFind(tr, br, delegate(string tag)
                        {
                            return IsNumberTag(tag, tag.ToUpperInvariant());
                        });
                    ftitle = AttrByTags(tr, br, Cfg.NameTags);
                    if (ftitle == null)
                        ftitle = AttrFind(tr, br, delegate(string tag)
                        {
                            return IsNameTag(tag, tag.ToUpperInvariant());
                        });
                    string sval = AttrByTags(tr, br, Cfg.ScaleTags);
                    if (sval == null)
                        sval = AttrFind(tr, br, delegate(string tag)
                        {
                            string u = tag.ToUpperInvariant();
                            return tag.Contains("比例") || u.Contains("SCALE");
                        });
                    frameScale = ParseScaleText(sval);
                }
                else
                {
                    Polyline pl = ent as Polyline;
                    if (pl != null)
                    {
                        if (!pl.Closed || pl.NumberOfVertices < 4) continue;
                        if (autoMode)
                        {
                            // 自动模式要求近似矩形（面积/外接框面积>0.95），排除圆和弧形轮廓
                            Extents3d tx = pl.GeometricExtents;
                            double bb = (tx.MaxPoint.X - tx.MinPoint.X) * (tx.MaxPoint.Y - tx.MinPoint.Y);
                            if (bb <= 0 || pl.Area / bb < 0.95) continue;
                            // 最短边阈值：过滤小房间/设备矩形等伪图框
                            double shortSide = Math.Min(tx.MaxPoint.X - tx.MinPoint.X, tx.MaxPoint.Y - tx.MinPoint.Y);
                            if (Cfg.MinPolylineSide > 0 && shortSide < Cfg.MinPolylineSide) continue;
                        }
                    }
                    else if (autoMode)
                    {
                        continue;   // 自动模式只认图块和多段线
                    }
                }

                Extents3d ex;
                try { ex = ent.GeometricExtents; }
                catch { continue; }   // 无外接框的实体（构造线等）
                if (ex.MaxPoint.X - ex.MinPoint.X <= 0 || ex.MaxPoint.Y - ex.MinPoint.Y <= 0) continue;

                FrameInfo fi = new FrameInfo();
                fi.MinX = ex.MinPoint.X; fi.MinY = ex.MinPoint.Y;
                fi.MaxX = ex.MaxPoint.X; fi.MaxY = ex.MaxPoint.Y;
                fi.Number = fnum;
                fi.Title = ftitle;
                fi.Scale = frameScale;
                fi.SourceId = ent.Id;
                fi.FromAuto = autoMode;
                frames.Add(fi);
            }
            return frames;
        }

        void DropContained(List<FrameInfo> frames)
        {
            frames.Sort(delegate(FrameInfo a, FrameInfo b) { return b.AreaX.CompareTo(a.AreaX); });
            List<FrameInfo> kept = new List<FrameInfo>();
            foreach (FrameInfo f in frames)
            {
                double eps = Math.Max(f.MaxX - f.MinX, f.MaxY - f.MinY) * 1e-4;
                bool inside = false;
                foreach (FrameInfo k in kept)
                {
                    if (f.MinX >= k.MinX - eps && f.MaxX <= k.MaxX + eps &&
                        f.MinY >= k.MinY - eps && f.MaxY <= k.MaxY + eps)
                    { inside = true; break; }
                }
                if (!inside) kept.Add(f);
            }
            frames.Clear();
            frames.AddRange(kept);
        }

        string EffectiveName(Transaction tr, BlockReference br)
        {
            // 动态块取原始块名（匿名 *U 名称不含关键字，会漏识别）
            ObjectId id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
            BlockTableRecord btr = tr.GetObject(id, OpenMode.ForRead) as BlockTableRecord;
            return btr == null ? "" : btr.Name;
        }

        // "TK" 需左侧为词边界（TK_A1/TK841 命中；STACK/OUTKIT 等子串不再误伤）
        static Regex TkRx = new Regex(@"(?:^|[^A-Za-z])TK", RegexOptions.IgnoreCase);

        bool IsFrameName(string name)
        {
            if (name == null || name.Length == 0) return false;
            string u = name.ToUpperInvariant();
            return name.Contains("图框") || TkRx.IsMatch(name)
                || u.Contains("FRAME") || u.Contains("TITLEBLOCK") || name.Contains("标题栏");
        }

        // 图号/图名标签判定（CollectFrames 兜底扫描与 BPTEACH 分类共用）
        // 图号排除 TITLE/NAME（DRAWINGTITLE 不再被误归为图号）；"NO" 仅匹配词尾（NOTES 不再误归，DWGNO/SHEETNO 仍命中）
        static bool IsNumberTag(string tag, string u)
        {
            if (tag.Contains("图号")) return true;
            if (u.Contains("TITLE") || u.Contains("NAME") || tag.Contains("图名")) return false;
            return u.Contains("DWG") || u.EndsWith("NO", StringComparison.Ordinal);
        }

        static bool IsNameTag(string tag, string u)
        {
            return tag.Contains("图名") || u.Contains("SHEET") || u.Contains("TITLE");
        }

        // 从图框属性取文件名：优先 图号 → 图名 → SHEET/编号
        // 按配置标签精确匹配取值（大小写不敏感）
        string AttrByTags(Transaction tr, BlockReference br, List<string> tags)
        {
            if (tags == null) return null;
            foreach (string want in tags)
            {
                foreach (ObjectId id in br.AttributeCollection)
                {
                    AttributeReference ar = tr.GetObject(id, OpenMode.ForRead) as AttributeReference;
                    if (ar == null) continue;
                    if (string.Equals(ar.Tag, want, StringComparison.OrdinalIgnoreCase))
                    {
                        string v = ar.TextString == null ? "" : ar.TextString.Trim();
                        if (v.Length > 0) return v;
                    }
                }
            }
            return null;
        }

        // 按谓词扫描属性标签，取第一个非空值
        string AttrFind(Transaction tr, BlockReference br, Predicate<string> match)
        {
            foreach (ObjectId id in br.AttributeCollection)
            {
                AttributeReference ar = tr.GetObject(id, OpenMode.ForRead) as AttributeReference;
                if (ar == null || ar.Tag == null) continue;
                if (!match(ar.Tag)) continue;
                string v = ar.TextString == null ? "" : ar.TextString.Trim();
                if (v.Length > 0) return v;
            }
            return null;
        }

        // 解析比例分母：接受 1:100 / 1：100 / 1/100 / 100；无效返回 0（用全局）
        static Regex ScaleRx = new Regex(@"^1\s*[:：/]\s*(\d+)$");

        int ParseScaleText(string v)
        {
            if (v == null) return 0;
            v = v.Trim();
            if (v.Length == 0) return 0;
            Match m = ScaleRx.Match(v);
            int n;
            if (m.Success)
            {
                // TryParse：属性值异常（如超长数字）不再抛 OverflowException 中断整批
                if (int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)
                    && n >= 1 && n <= 100000) return n;
            }
            else if (int.TryParse(v, out n) && n >= 1 && n <= 100000) return n;
            return 0;
        }

        static bool ListContainsI(List<string> list, string s)
        {
            foreach (string t in list)
                if (string.Equals(t, s, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static bool TagMatch(List<string> tags, string tag)
        {
            return ListContainsI(tags, tag);
        }

        // 黑白样式查找（只查一次）
        string FindCtb(PlotSettingsValidator val)
        {
            try
            {
                foreach (string s in val.GetPlotStyleSheetList())
                    if (string.Equals(s, "monochrome.ctb", StringComparison.OrdinalIgnoreCase)) return s;
            }
            catch { }
            return null;
        }

        // ---------- 纸张表 ----------
        static Regex MediaRx = new Regex(@"(\d+(?:\.\d+)?)_x_(\d+(?:\.\d+)?)");

        Media ParseMedia(string name)
        {
            if (name == null || !name.ToUpperInvariant().EndsWith("MM)")) return null;
            Match m = MediaRx.Match(name);
            if (!m.Success) return null;
            Media md = new Media();
            md.CanonicalName = name;
            md.W = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            md.H = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            return md;
        }

        List<Media> ParseMediaList(StringCollection names)
        {
            List<Media> list = new List<Media>();
            if (names == null) return list;
            foreach (string n in names)
            {
                Media md = ParseMedia(n);
                if (md != null) list.Add(md);
            }
            return list;
        }

        static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        string Sanitize(string s)
        {
            char[] bad = Path.GetInvalidFileNameChars();
            string r = s.Trim();
            foreach (char c in bad) r = r.Replace(c, '_');
            r = r.TrimEnd('.', ' ');   // Windows 文件名不允许以点/空格结尾
            string stem = r.Split('.')[0];
            if (ReservedNames.Contains(stem)) r = "_" + r;   // CON/NUL 等保留名前加下划线
            if (r.Length > MaxNameLen) r = r.Substring(0, MaxNameLen).TrimEnd('.', ' ');
            return r.Length == 0 ? "Sheet" : r;
        }

        // 默认输出目录：DWG 所在目录；未保存图纸（无路径）回退到"我的文档"，
        // 避免相对路径落到 AutoCAD 当前工作目录（安装目录/随机位置）
        static string DefaultOutDir(Database db, string sub)
        {
            string dir = null;
            try { dir = Path.GetDirectoryName(db.Filename); } catch { }
            if (string.IsNullOrEmpty(dir))
                dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(dir, sub);
        }
    }
}
