# BPPlot 后续开发设计方案

状态：定稿（2026-09）｜基线：v0.6（commit 79b4a7d 之后）｜预计总量：约 18 人日

功能对标参考 MSteel 批打印（本机 2025-04 版官方文档逐条核对）。**明确不做**：签名/印章库、
实体打印机纸张输出（设备固定 `DWG To PDF.pc3`）。MSteel 的"五种框选方式"与打印目标无关，
作为选择体验吸收进 M1。

---

## 1. 目标与范围

补齐 v0.6 与成熟批打印工具的功能差距，同时保持三条既有优势：开源 MIT / accoreconsole
无头批打 / 矢量裁切任意图幅。

| 差距项 | 处置 | 归属模块 |
|---|---|---|
| 出图预览 | 实现（对话框预览列 + 预检高亮） | M5 |
| 输出格式（DWF/JPG/PNG/PLT/EPS + 背景色） | 实现 | M3 |
| 对话框批量编辑（多行改/表头统一改/图号重编/重复标红） | 实现 | M5 |
| 图框定制体系（模板库/打印范围微调/排序优先级/导入导出） | 实现 | M2 |
| 目录导出 Excel | 实现（零依赖 SpreadsheetML 优先） | M4 |
| 打印进度反馈 | 实现（PlotProgress + 取消） | M5 |
| 多 DWG 目录遍历 | 实现（串行 bat + 并行编排器） | M6 |
| 质量辅助（真彩→索引/文字填充/分辨率/直线合并） | 实现 | M7 |
| 平台覆盖 | AutoCAD **2013–2024 + 2025–2027** 双产物 | M8 |
| 签名/印章库 | **不做** | — |
| 实体打印机输出 | **不做** | — |

## 2. 模块划分与代码结构

现 `BPPlot.cs`（约 1600 行单文件）拆分为：

```
src/
  Config.cs          — BPConfig v2（模板库 + 命名规则 + 格式档案）
  FrameInfo.cs       — 数据结构（FrameInfo/EntBox/Media）
  FrameCollector.cs  — M1 图框识别与选择
  FrameTemplate.cs   — M2 模板匹配/排序/inset
  PlotRunner.cs      — M3 出图引擎（PlotEngine 封装 + PlotProgress）
  FormatProfiles.cs  — M3 设备-格式矩阵 + Pc3Factory
  PageBuilder.cs     — M3 BuildPage（现有逻辑迁移）
  PdfCrop.cs         — M4 裁切（现有 CropPdf 迁移）
  PdfMerge.cs        — M4 合并（现有 MergePdfs 迁移）
  SheetIndex.cs      — M4 目录导出（CSV + SpreadsheetML）
  BpForm.cs          — M5 主对话框（增强）
  PreviewPane.cs     — M5 缩略图预览
  ProgressForm.cs    — M5 进度对话框
  QualityKit.cs      — M7 真彩转换/出图变量包裹
batch/
  runall.bat         — M6 串行批打 v1
  BpBatch/           — M6 并行编排器（独立 exe，不依赖 AutoCAD）
```

## 3. 各模块设计

### M1 图框识别与选择

| 功能 | 说明 | 实现要点 |
|---|---|---|
| 五种选择方式 | ①指定图块（全图块名下拉/图中拾取）②指定图层封闭矩形 ③自选封闭矩形 ④两点定框 ⑤图集（每选一批回车=一框） | ①②做 `BPLOTAUTO` 过滤参数；③④⑤挂 `BPLOT` 交互。统一产出 `FrameInfo`，后续管线不变 |
| 去重/包含剔除 | 已有 `DropContained` | 不动 |

### M2 图框模板与信息（config.json v2，向后兼容）

```json
{
  "templates": [{
    "block": "TK-A1",
    "tags":  { "number":"图号", "title":"图名", "scale":"比例", "rev":"版次", "date":"日期" },
    "sortPriority": 2,
    "inset":  { "left":0, "right":0, "top":0, "bottom":0 },
    "infoCentering": false
  }],
  "fileTemplate": "{图号}_{图名}"
}
```

- **动态图框**：v0.6 已按每个 INSERT 实际外接框计算，天然满足"录一个成员即可"
- **inset 打印范围微调**：在 `BuildPage` 的窗口四边应用（v0.6 负留白已验证同一路径），用于剔除图框角落不需打印的部分
- **排序**：出图顺序 = 模板 `sortPriority` → 图号自然排序（`A2 < A10`）
- **信息居中**（可选，低频）：BPREV 写入后按属性包围盒重算 `Position`
- **导入导出**：config.json 本就可拷贝，对话框加按钮

### M3 出图引擎与格式矩阵

| 格式 | 设备（AutoCAD 自带 pc3） | 裁切到真实图幅 |
|---|---|---|
| PDF | `DWG To PDF.pc3`（现有） | ✅ 现有 CropPdf |
| DWF | `DWF6 ePlot.pc3` | ❌（页=纸） |
| JPG/PNG | `PublishToWeb JPG/PNG.pc3` | ❌（光栅） |
| PLT | HPGL 系 pc3 | ❌ |
| EPS | `Generic PostScript.pc3` | ❌ |

- **背景色 / 矢量分辨率 / 直线合并**均为 pc3 自定义属性，`PlotSettingsValidator` 不暴露 →
  `Pc3Factory` 程序化生成 pc3 变体（如 `BPPlot_黑底JPG.pc3`，装入用户 PlotStyles 目录），
  运行时按格式档案选择。`libs/pc3_dwg2pdf_body.bin`（此前 pc3 体实验遗留）作种子。
- 裁切按 `FormatProfiles.CanCrop` 开关；光栅格式跳过 CropPdf 并在 CSV 标注实际纸幅。
  **"任意尺寸纸张"承诺仅 PDF/DWF 成立**，README 注明。

### M4 后处理与目录

- **Excel 目录**：零依赖方案 SpreadsheetML 2003（XML 文本即 .xls，支持表头着色/列宽）；
  需真 .xlsx 再上 DocumentFormat.OpenXml（MIT，net462 可用）
- **多 DWG 全局合并**：纯 PdfSharp 操作（放 M6，不占 AutoCAD）

### M5 对话框交互

| 功能 | 实现要点 |
|---|---|
| 预览列（点行看缩略图） | 首选 GS 离屏渲染（`GraphicsSystem` 按图框 extents 渲染位图，GUI 模式快且无闪屏）；控制台/兜底走低分辨率 PNG 试打（同一 PlotEngine 管线）。内存缓存，选中行懒加载 |
| BPCHECK 预检高亮 | 识别框临时图层高亮+序号，逐框缩放查看，退出即删（预览最小形态，P0 先行交付） |
| 多行批量编辑 | DataGridView 多选（借不可编辑的序号列选行）+ 右键菜单改纸张/比例/版次/日期 |
| 表头统一改 | 点「纸张尺寸」「打印比例」列头全表统一 |
| 图号重编 | 选行→点图号列重编；点列头全表重编；回写块属性（BPREV 事务范式）+刷新单元格 |
| 重复标红 | 把 `usedNames` 去重判断前移到 `FillGrid`，图号/文件名重复行整行红字，实时 |
| 进度对话框 | 实现 `PlottingServices.PlotProgress` 挂 `BeginPlot`；取消=完成当前页后停，已出页保留 |

### M6 批处理调度

| 功能 | 说明 |
|---|---|
| 串行批打 v1 | `runall.bat`：`for %%f in (*.dwg)` 循环 accoreconsole，逐文件日志 + `失败清单.txt` |
| 并行编排器 | `BpBatch.exe`：独立控制台进程，信号量限 2–4 并发；**每 DWG 独立临时输出目录**（防 CSV/PDF 互踩）完成后归并；并发数=license 占用数，默认串行、并行显式开 |
| 试跑模式 | `--sample N` 先出 N 张供人核，确认后续全量（无头场景的"预览确认"替代） |
| 全局合并 | 收尾 PdfSharp 拼总 PDF（两级书签：DWG 名 → 图号_图名） |

### M7 质量辅助

| 功能 | 实现 |
|---|---|
| 真彩→索引色 | `BPCOLOR`：遍历实体，`Color.ColorMethod != ByAci` 时查最近 ACI（24bit→256 最近邻）改 `Entity.Color`；跳过 ByLayer/ByBlock（黑白样式表只作用索引色，此为其前置修复） |
| 文字填充 | 出图期 `TEXTFILL=1` 后恢复（BACKGROUNDPLOT 同款包裹）；完整"文字转图形"需 TXTEXP，重、可选 |
| 分辨率/直线合并/背景色 | pc3 属性 → 归并 M3 Pc3Factory |

### M8 平台适配：AutoCAD 2013–2027，双产物

| 编译产物 | 工具链 | 引用 SDK | 覆盖版本 |
|---|---|---|---|
| `BPPlot.dll`（主产物） | csc v4.0.30319（.NET Framework 4.0） | **ObjectARX 2013** | **2013–2024** |
| `BPPlot-net8.dll` | dotnet build（net8.0-windows） | ObjectARX 2025 | **2025–2027** |

- 2013 基线卡在两处边界：accoreconsole 2013 才引入（全支持域无头可用）；`DWG To PDF.pc3` 2010 起内置。对最老版本编译、向新版本前向加载是 Autodesk 官方路径
- net8 构建：2025 起 `Application` → `Autodesk.AutoCAD.ApplicationServices.Core`，`#if ACAD_NET8` 别名层（约 10 处）；PdfSharp 双轨（主产物 1.x，net8 产物 6.x，源码共用）
- Release 包含两对 DLL（各自配套 PdfSharp）+ 版本对照表
- **验证边界（如实声明）**：本机仅 AutoCAD 2023——主产物在 2023 实测（证明 2013 基线前向加载成立）；net8 产物本机可编译（.NET 8 SDK + ObjectARX 2025 SDK，均免费），运行验证需 2025+ 机器
- 功能矩阵：全支持域 GUI 全命令 ✅ / 无头 ✅ / PDF 管线 ✅（2013 起无版本缺口）

## 4. 实施分期

| 期 | 内容 | 工作量 | 验收标准 |
|---|---|---|---|
| **P0 地基+速赢** | 代码拆分；config v2（向后兼容）；重复标红；图号自然排序+模板优先级；BPCHECK 预检高亮；SpreadsheetML Excel 目录 | ~2d | 现有 accoreconsole 回归全绿（竖版/1:150/合并）；标红用真实图验证 |
| **P1 多 DWG 批打** | runall.bat；BpBatch.exe 并行+试跑+全局合并 | ~2.5d | 20 个 DWG 目录批跑：失败清单准确、并行 3 无互踩、总 PDF 两级书签 |
| **P2 格式矩阵** | FormatProfiles + Pc3Factory；DWF/JPG/PNG/EPS 打通；裁切按格式开关 | ~3.5d | 每格式样张目检；黑底 JPG 背景色正确 |
| **P3 对话框增强** | 多行编辑/表头统一/图号重编 | ~2d | 50 框图批量改比例→CSV 与 PDF 一致 |
| **P4 预览面板** | GS 缩略图 + PNG 兜底 + 缓存 | ~2d | 大图（7 万实体）首帧 <2s，切换 <0.3s |
| **P5 模板强化** | inset 范围微调、导入导出、信息居中（可选） | ~2d | 角部剔除案例实测 |
| **P6 质量辅助** | BPCOLOR、TEXTFILL 包裹（分辨率等已在 P2） | ~1.5d | 真彩图转索引后黑白打印正确 |
| **P7 平台适配** | 下载 ObjectARX 2013/2025 SDK；主产物改 2013 基线重编译 + 2023 前向兼容回归；net8 条件编译 + PdfSharp 6 + build_net8.cmd；README/Release 改版 | ~2.5d | 2013 基线 DLL 在 2023 全回归通过；net8 编译通过 |

顺序理由：P1 是路线图头号待办且最便宜；P4（预览）依赖 P0 的 BPCHECK 与 P3 的列表重构，后置一次到位。

## 5. 风险与对策

| 风险 | 对策 |
|---|---|
| pc3 程序化生成（P2 唯一深水区，OLE 复合文档） | 先二进制 diff 探路；失败退化为"预生成变体库随插件分发" |
| 光栅格式无 MediaBox | 文档明确"任意尺寸纸张"仅 PDF/DWF 成立 |
| 并行受 license 数限制 | BpBatch 默认串行，并行显式开并提示 license 占用 |
| 老 SDK 下载渠道（ObjectARX 2013/2025） | Autodesk 官网免费，需注册开发者账号；若失效改为"引用集缓存入库" |
| net8 产物无本机运行环境 | 编译期保证 + 文档注明"2025+ 机器冒烟后发布" |

## 6. 测试策略

复用现有 accoreconsole 回归资产（`test/fixtest.scr` 模式）：

- 每期结束跑既有矩阵：竖版 A1 / 横版加长 B0 / 1:150 / 1:1000 / 合并书签 / 真实工程图全量
- 新增：格式矩阵每格式一张样张目检（pymupdf 渲染 + 墨迹 bbox 量化，方法同 v0.6 取证）
- 多 DWG：合成 20 个小 DWG 目录批跑，校验失败清单与归并结果
- `BPPLOT_KEEP_ORIG=1` 继续用于裁切/格式问题诊断

## 7. v0.6 现状基线（新模块挂点）

| 现有能力 | 位置 | 增强落点 |
|---|---|---|
| 图框收集（模型空间过滤/动态块/去重） | `CollectFrames` | M1 五种选择方式 |
| 图框学习 config.json | `BPConfig`/`BPTEACH` | M2 模板 v2 |
| 页面构建（选纸/比例/旋转） | `BuildPage`/`PickMedia` | M3 inset/格式档案 |
| 三阶段出图（构建→打印→裁切合并） | `PlotFrames2` | M3/M4 拆分迁移 |
| 出图对话框 | `BpForm` | M5 全部增强 |
| CSV 目录 + 纸张统计 | `WriteCsvAndStats` | M4 Excel |
| 旋转变换标定（rot=90 反转置） | `CropPdf` | 迁移不动（核心资产） |
