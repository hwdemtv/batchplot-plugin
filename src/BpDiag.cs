// BpDiag.cs — 真实图纸结构诊断（只读，不改图）
// 加载即运行：实体类普查 / 块使用与属性定义 / 布局清单 / 类字典中的天正类
// 输出到 USERS2 指定的文本文件

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(BpDiag.App))]

namespace BpDiag
{
    public class App : IExtensionApplication
    {
        public void Initialize()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            string outPath = "";
            try
            {
                object u2 = Application.GetSystemVariable("USERS2");
                if (u2 != null) outPath = u2.ToString().Trim();
            }
            catch { }
            if (outPath.Length == 0) return;

            StringBuilder sb = new StringBuilder();
            try
            {
                Database db = doc.Database;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 1. ModelSpace 实体普查（天正自定义对象以代理实体出现，提取其原始类名）
                    Dictionary<string, int> census = new Dictionary<string, int>();
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        string cls;
                        if (ent == null) cls = "<null>";
                        else
                        {
                            cls = ent.GetRXClass().Name;
                            ProxyEntity pe2 = ent as ProxyEntity;
                            if (pe2 != null)
                            {
                                string orig = "";
                                try { orig = pe2.OriginalClassName; } catch { }
                                if (orig.Length == 0)
                                {
                                    try { orig = pe2.OriginalDxfName; } catch { }
                                }
                                cls = "PROXY[" + orig + "]";
                            }
                        }
                        if (cls.Length == 0) cls = "<" + ent.GetType().FullName + ">";
                        if (!census.ContainsKey(cls)) census[cls] = 0;
                        census[cls]++;
                    }
                    sb.AppendLine("=== ModelSpace 实体普查 ===");
                    foreach (KeyValuePair<string, int> kv in census)
                        sb.AppendLine(kv.Value + "\t" + kv.Key);

                    // 2. 块使用统计 + 属性定义（ATTDEF 标签）
                    sb.AppendLine();
                    sb.AppendLine("=== 块使用与属性定义 ===");
                    Dictionary<ObjectId, int> inserts = new Dictionary<ObjectId, int>();
                    foreach (ObjectId id in ms)
                    {
                        BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br == null) continue;
                        if (!inserts.ContainsKey(br.BlockTableRecord)) inserts[br.BlockTableRecord] = 0;
                        inserts[br.BlockTableRecord]++;
                    }
                    foreach (KeyValuePair<ObjectId, int> kv in inserts)
                    {
                        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(kv.Key, OpenMode.ForRead);
                        List<string> tags = new List<string>();
                        foreach (ObjectId aid in btr)
                        {
                            AttributeDefinition ad = tr.GetObject(aid, OpenMode.ForRead) as AttributeDefinition;
                            if (ad != null)
                                tags.Add(ad.Tag + (ad.Constant ? "(常量)" : "") + "=[" + (ad.TextString ?? "") + "]");
                        }
                        sb.AppendLine("BLOCK\t" + kv.Value + "x\t" + btr.Name
                            + (btr.IsDynamicBlock ? " [动态]" : "")
                            + "\t" + string.Join(", ", tags.ToArray()));
                    }

                    // 3. 布局清单
                    sb.AppendLine();
                    sb.AppendLine("=== 布局 ===");
                    DBDictionary layDict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                    foreach (DBDictionaryEntry de in layDict)
                    {
                        Layout lay = (Layout)tr.GetObject(de.Value, OpenMode.ForRead);
                        sb.AppendLine(lay.LayoutName + "\t纸张=" + lay.CanonicalMediaName
                            + "\t设备=" + lay.PlotConfigurationName);
                    }

                    // 3.5 图层状态 + 图框窗口内可见实体抽查（验证空页=冻结层假设）
                    sb.AppendLine();
                    sb.AppendLine("=== 图层状态 ===");
                    Dictionary<string, bool> layFrozen = new Dictionary<string, bool>();
                    LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    foreach (ObjectId lid2 in lt)
                    {
                        LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lid2, OpenMode.ForRead);
                        layFrozen[ltr.Name] = ltr.IsFrozen || ltr.IsOff;
                        if (ltr.IsFrozen || ltr.IsOff)
                            sb.AppendLine("FROZEN/OFF\t" + ltr.Name);
                    }
                    sb.AppendLine();
                    sb.AppendLine("=== 图框窗口可见性抽查（前6个带属性图框）===");

                    // 收集模型空间实体（图层名+extents）一次，供窗口检查复用
                    List<ObjectId> allIds = new List<ObjectId>();
                    foreach (ObjectId id in ms) allIds.Add(id);

                    int checkedFrames = 0;
                    foreach (ObjectId id in allIds)
                    {
                        if (checkedFrames >= 6) break;
                        BlockReference br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br == null) continue;
                        // 只查带属性的图框块
                        bool hasAttr = false;
                        foreach (ObjectId aid in br.AttributeCollection)
                        {
                            if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference) { hasAttr = true; break; }
                        }
                        if (!hasAttr) continue;
                        Extents3d ex;
                        try { ex = br.GeometricExtents; } catch { continue; }
                        checkedFrames++;
                        int vis = 0, hid = 0;
                        Dictionary<string, int> hidLayers = new Dictionary<string, int>();
                        foreach (ObjectId eid in allIds)
                        {
                            Entity ent = tr.GetObject(eid, OpenMode.ForRead) as Entity;
                            if (ent == null) continue;
                            Extents3d e2;
                            try { e2 = ent.GeometricExtents; } catch { continue; }
                            if (e2.MinPoint.X > ex.MaxPoint.X || e2.MaxPoint.X < ex.MinPoint.X ||
                                e2.MinPoint.Y > ex.MaxPoint.Y || e2.MaxPoint.Y < ex.MinPoint.Y) continue;
                            string ln = ent.Layer;
                            bool hidden = false;
                            layFrozen.TryGetValue(ln, out hidden);
                            if (hidden)
                            {
                                hid++;
                                if (!hidLayers.ContainsKey(ln)) hidLayers[ln] = 0;
                                hidLayers[ln]++;
                            }
                            else vis++;
                        }
                        StringBuilder hls = new StringBuilder();
                        int taken = 0;
                        foreach (KeyValuePair<string, int> kv2 in hidLayers)
                        {
                            if (taken++ >= 6) { hls.Append(" ..."); break; }
                            hls.Append(kv2.Key).Append('(').Append(kv2.Value).Append(") ");
                        }
                        sb.AppendLine("FRAME " + EffectiveBlockName(tr, br) +
                            "\t窗口内可见实体=" + vis + "\t隐藏实体=" + hid + "\t隐藏层: " + hls.ToString());
                    }

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                sb.AppendLine("DIAG ERROR: " + ex.GetType().Name + ": " + ex.Message);
            }
            try
            {
                File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
            }
            catch (System.Exception ex2)
            {
                // USERS2 指向目录/非法路径时不再抛未处理异常
                try { doc.Editor.WriteMessage("\nBPDiag 写出失败(" + outPath + "): " + ex2.Message + "\n"); } catch { }
            }
        }

        static string EffectiveBlockName(Transaction tr, BlockReference br)
        {
            try
            {
                ObjectId bid = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bid, OpenMode.ForRead);
                return btr.Name;
            }
            catch { return "?"; }
        }

        public void Terminate() { }
    }
}
