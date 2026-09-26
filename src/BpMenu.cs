// BpMenu.cs — 菜单栏集成（对齐 M-批打印 的使用习惯）
// NETLOAD 后自动在菜单栏挂「BP-批量打印」菜单，命令与命令行入口完全一致。
// 实现分两步：
//   1. AcCui.dll（Managed Customization API）运行时生成部分菜单 %APPDATA%\BPPlot\BPPlot.cui
//   2. COM MenuGroups.Load 加载该 CUI，InsertInMenuBar 挂到菜单栏末尾
// 持久化语义：部分菜单会跨会话保留，但插件本身不会自动加载——因此每项宏都自带
// "静默 NETLOAD 当前插件路径"前缀（NETLOAD 对已加载程序集是幂等空操作），重启 CAD 后
// 点击菜单即自动加载插件并执行；每次 NETLOAD 时就地刷新已挂菜单的宏，保证路径始终最新。
// 仅图形界面有效（accoreconsole 无图形环境，App.Initialize 已按进程名跳过）；
// 菜单栏默认随功能区隐藏，看不到菜单时执行 MENUBAR 输入 1。

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

        // 菜单定义（显示标签, 命令名）——生成 CUI 与就地刷新宏共用
        static readonly string[,] Items = new string[,]
        {
            { "单张打印预览", "BPPREVIEW" },
            { "单张快打到 PDF", "BP1" },
            { "批量出图对话框（勾选确认）", "BPLOT" },
            { "全自动批量出图", "BPLOTAUTO" },
            { "批量出图并合成单 PDF", "BPLOTMERGE" },
            { "布局批量出图", "BPL" },
            { "DWG 按图框拆分", "BPSPLIT" },
            { "图框学习", "BPTEACH" },
            { "批量改版次/日期", "BPREV" },
            { "使用说明（GitHub）...", "BPHELP" },
            { "关于 BPPlot...", "BPABOUT" }
        };

        public static void Install()
        {
            try
            {
                AcadApplication app = (AcadApplication)Application.AcadApplication;
                if (app == null) return;

                // 已加载的同名菜单组：就地刷新宏（DLL 挪动/升级后路径保持最新），需要时重新挂栏
                AcadMenuGroup existing = FindGroup(app);
                if (existing != null)
                {
                    AcadPopupMenu popup = FindPopup(existing);
                    if (popup != null)
                    {
                        RefreshMacros(popup);
                        if (!popup.OnMenuBar) popup.InsertInMenuBar(app.MenuBar.Count);
                        Notice(app, "已刷新菜单: " + MenuTitle);
                        return;
                    }
                }

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
                        Notice(app, "已加载菜单: " + MenuTitle);
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

        static AcadMenuGroup FindGroup(AcadApplication app)
        {
            foreach (AcadMenuGroup g in app.MenuGroups)
                if (g.Name == MenuGroupName) return g;
            return null;
        }

        static AcadPopupMenu FindPopup(AcadMenuGroup grp)
        {
            foreach (AcadPopupMenu p in grp.Menus)
                if (p.Name == MenuTitle) return p;
            return null;
        }

        // 就地刷新：按标签匹配逐项 set_Macro（宏内含当前插件路径）
        static void RefreshMacros(AcadPopupMenu popup)
        {
            foreach (AcadPopupMenuItem it in popup)
            {
                for (int i = 0; i < Items.GetLength(0); i++)
                {
                    if (it.Label == Items[i, 0])
                    {
                        it.Macro = SelfLoadMacro() + "_" + Items[i, 1] + " ";
                        break;
                    }
                }
            }
        }

        // 运行时重建部分菜单 CUI（文档模式：无参构造 + 设组名 + SaveAs）
        static void GenerateCui(string cui)
        {
            if (File.Exists(cui)) File.Delete(cui);
            CustomizationSection cs = new CustomizationSection();
            cs.MenuGroup.Name = MenuGroupName;
            MenuGroup mg = cs.MenuGroup;
            MacroGroup mac = new MacroGroup(MenuGroupName + " commands", mg);

            PopMenu pm = new PopMenu(MenuTitle, new StringCollection(), MenuTitle + "Tag", mg);
            pm.Aliases.Add("BPPOP");
            for (int i = 0; i < Items.GetLength(0); i++)
            {
                MenuMacro mm = new MenuMacro(mac, Items[i, 0],
                    SelfLoadMacro() + "_" + Items[i, 1] + " ", "ID_" + Items[i, 1]);
                new PopMenuItem(mm, Items[i, 0], pm, pm.PopMenuItems.Count);
            }
            if (!cs.SaveAs(cui)) throw new System.Exception("部分菜单写入失败: " + cui);
        }

        // 菜单项宏前缀：先确保插件已加载再执行命令。
        // NETLOAD 对已加载程序集是幂等空操作；路径取自程序集实际位置（正斜杠避免转义问题），
        // FILEDIA 临时置 0 以免 NETLOAD 弹文件对话框（原值用局部变量恢复）。
        static string SelfLoadMacro()
        {
            string dll = typeof(BpMenu).Assembly.Location.Replace('\\', '/');
            return "^C^C^P(progn (setq bp_fd (getvar \"filedia\")) (setvar \"filedia\" 0)" +
                   " (command \"_.NETLOAD\" \"" + dll + "\") (setvar \"filedia\" bp_fd));";
        }

        static void Notice(AcadApplication app, string msg)
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null) doc.Editor.WriteMessage("\n" + msg + "\n");
            }
            catch { }
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
                "License: MIT");
        }
    }
}
