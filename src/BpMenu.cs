// BpMenu.cs — 菜单栏集成（对齐 M-批打印 的使用习惯）
// NETLOAD 后自动在菜单栏挂「BP-批量打印」菜单，命令与命令行入口完全一致。
// 实现分两步：
//   1. AcCui.dll（Managed Customization API）运行时生成部分菜单 %APPDATA%\BPPlot\BPPlot.cui
//   2. COM MenuGroups.Load 加载该 CUI，InsertInMenuBar 挂到菜单栏末尾
// 仅图形界面有效（accoreconsole 无图形环境，App.Initialize 已按进程名跳过）；
// 重复 NETLOAD / 启动套件每次启动都会调用，已挂时自动去重。
// 注意：菜单栏默认随功能区隐藏，看不到菜单时在命令行执行 MENUBAR 并输入 1。

using System;
using System.Collections.Specialized;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Customization;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Interop;

namespace BpPlot
{
    public class BpMenu
    {
        const string MenuGroupName = "BPPlot";
        const string MenuTitle = "BP-批量打印";

        public static void Install()
        {
            try
            {
                AcadApplication app = (AcadApplication)Application.AcadApplication;
                if (app == null) return;

                // 已挂在菜单栏则跳过（防重复 NETLOAD）
                foreach (AcadPopupMenu m in app.MenuBar)
                    if (m.Name == MenuTitle) return;

                string cui = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BPPlot", "BPPlot.cui");
                Directory.CreateDirectory(Path.GetDirectoryName(cui));
                GenerateCui(cui);

                AcadMenuGroup grp = app.MenuGroups.Load(cui);
                foreach (AcadPopupMenu p in grp.Menus)
                {
                    if (p.Name == MenuTitle)
                    {
                        p.InsertInMenuBar(app.MenuBar.Count);
                        Document doc = Application.DocumentManager.MdiActiveDocument;
                        if (doc != null) doc.Editor.WriteMessage("\n已加载菜单: " + MenuTitle + "\n");
                        break;
                    }
                }
            }
            catch (System.Exception ex)
            {
                try
                {
                    Document doc = Application.DocumentManager.MdiActiveDocument;
                    if (doc != null) doc.Editor.WriteMessage("\nBP-批量打印 菜单加载失败: " + ex.Message + "\n");
                }
                catch { }
            }
        }

        // 运行时重建部分菜单 CUI（覆盖生成，保证与当前命令集一致）
        static void GenerateCui(string cui)
        {
            if (File.Exists(cui)) File.Delete(cui);
            CustomizationSection cs = new CustomizationSection(cui, MenuGroupName);
            MenuGroup mg = cs.MenuGroup;
            MacroGroup mac = new MacroGroup(MenuGroupName + " commands", mg);

            PopMenu pm = new PopMenu(MenuTitle, new StringCollection(), MenuTitle + "Tag", mg);
            pm.Aliases.Add("BPPOP");
            AddCmd(mac, pm, "单张打印预览", "BPPREVIEW");
            AddCmd(mac, pm, "单张快打到 PDF", "BP1");
            AddCmd(mac, pm, "批量出图对话框（勾选确认）", "BPLOT");
            AddCmd(mac, pm, "全自动批量出图", "BPLOTAUTO");
            AddCmd(mac, pm, "批量出图并合成单 PDF", "BPLOTMERGE");
            AddCmd(mac, pm, "布局批量出图", "BPL");
            AddCmd(mac, pm, "DWG 按图框拆分", "BPSPLIT");
            AddCmd(mac, pm, "图框学习", "BPTEACH");
            AddCmd(mac, pm, "批量改版次/日期", "BPREV");
            AddCmd(mac, pm, "使用说明（GitHub）...", "BPHELP");
            AddCmd(mac, pm, "关于 BPPlot...", "BPABOUT");
            cs.Save();
        }

        static void AddCmd(MacroGroup mac, PopMenu pm, string label, string command)
        {
            MenuMacro mm = new MenuMacro(mac, label, "^C^C_" + command + " ", "ID_" + command);
            new PopMenuItem(mm, label, pm, pm.PopMenuItems.Count);
        }

        [CommandMethod("BPHELP")]
        public void BpHelp()
        {
            try
            {
                System.Diagnostics.Process.Start("https://github.com/hwdemtv/batchplot-plugin#readme");
            }
            catch (System.Exception)
            {
                Application.ShowAlertDialog("浏览器打开失败，请手动访问:\nhttps://github.com/hwdemtv/batchplot-plugin");
            }
        }

        [CommandMethod("BPABOUT")]
        public void BpAbout()
        {
            Application.ShowAlertDialog(
                "BPPlot v0.7 — AutoCAD 批量打印插件\n" +
                "图框自动识别 · 精确比例批量出图 · 合并书签 · 无头批打\n\n" +
                "开源: github.com/hwdemtv/batchplot-plugin\n" +
                "License: MIT（无网络访问、无注册校验）");
        }
    }
}
