<div align="center">

# BPPlot — AutoCAD 批量打印插件

**免费开源 · 图框自动识别 · 精确比例批量出图 · DWG 批量转 PDF · 合并书签 · 无头批打**

[![Release](https://img.shields.io/github/v/tag/hwdemtv/batchplot-plugin?label=%E6%9C%80%E6%96%B0%E7%89%88&sort=semver)](https://github.com/hwdemtv/batchplot-plugin/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![AutoCAD](https://img.shields.io/badge/AutoCAD-2021--2024-red)](https://github.com/hwdemtv/batchplot-plugin)
[![Platform](https://img.shields.io/badge/.NET-Framework%204.x-blue)](https://github.com/hwdemtv/batchplot-plugin)

*English: A free & open-source **batch plot / batch print plugin for AutoCAD** (C#, .NET Framework 4.x,
AutoCAD 2021–2024). Auto-detects drawing frames / title blocks in model space, picks the smallest
suitable paper, plots **exact-scale PDFs** (incl. extended sheet sizes), merges them into one PDF
with bookmarks, exports a sheet schedule CSV, and runs **headless via accoreconsole**.*

**[下载 Releases](../../releases) · [使用说明](#使用) · [命令总表](#命令总表) · [常见问题](#踩坑记录对本机-autocad-2023-实测)**

**关键词**：AutoCAD 批量打印 · 批量出图 · DWG 转 PDF · 图框识别 · 天正图框 · 加长图幅 · A0/A1/A2 出图 ·
精确 1:n 比例 · 黑白打印 · 图纸目录 CSV · PDF 合并书签 · DWG 拆分 · accoreconsole 无头批打 · 批打印工具

</div>

---

PDF 合并用 PdfSharp（MIT 协议，libs/ 内）。

## v0.7 新增（当前开发版）

1. **菜单栏「BP-批量打印」**：NETLOAD 即自动挂到菜单栏（与 M-批打印 并列），11 项全命令入口 +
   使用说明/关于；纯代码实现（AcCui 运行时生成部分菜单 CUI + COM 挂载），无 .cuix 附件。
   看不到菜单栏时执行 `MENUBAR` 输入 `1`；重复 NETLOAD 自动去重。
2. **单张打印预览 BPPREVIEW**：选图框（回车=自动识别取最大图框）→ 弹出该图框的打印预览窗口，
   参数与正式出图完全一致（复用 BuildPage：纸张/比例/旋转/黑白）。基于 AutoCAD 预览引擎
   （`PlotFactory.CreatePreviewEngine`）实测打通：预览窗口弹出后阻塞等待人工关闭（ESC/点击），
   关闭后 EndPlot 正常收尾。这是「出图预览确认」待办的第一块基石；批量预览/确认后落盘待排期。
   仅图形界面可用（accoreconsole 无图形环境）。
3. 命令总表新增：`BPPREVIEW`（单张预览）、`BPHELP`（打开在线说明）、`BPABOUT`（关于）。

## 为什么写这个插件

- 天正/普通图框混排的图，商用批打印工具常**强制 A0/A1 缩放**，加长幅变形；BPPlot 自动选最小可容纸张并矢量裁切到真实图幅，**1:100 出来就是 1:100**
- 图纸空间与模型空间图框混排、空图框、重复图框，逐一手工挑很烦；BPPlot 自动识别 + 去重 + 跳空窗
- 出完整批图还要一份**图纸目录**（序号/图号/图名/纸张/A4 当量），送印估算直接用
- 无头 accoreconsole 批打整目录，**挂机出图不用守着 AutoCAD**

## 快速开始（30 秒版）

```text
1. 下载 Releases 里的 BPPlot-v0.6.zip，解压到任意目录（BPPlot.dll 与 PdfSharp.dll 须同目录）
2. AutoCAD 命令行： APPLOAD → 选 BPPlot.dll →（可加入启动套件）
3. 命令行输入 BPLOT  → 框选图框 → 回车 → 对话框里勾选 → 出图
   或 BPLOTAUTO → 自动识别全图图框直接出 PDF
```

## v0.6 修复（2026-09，真实图纸 + accoreconsole 实测验证）

1. **旋转页裁切修复（严重）**：旧版所有 rot=90 的页（竖版图框，如系统图/目录）裁切错位——
   内容被旋转 90°、两侧各裁掉约 120mm、上下留大片空白（v0.5 真实出图第 1~17 页全部如此，
   此前"25 页全部精确输出"的结论系目检误判）。实测标定：DWG To PDF 引擎在**名义纸**
   （纸型名的原始宽高）上把图面按 90° 转置居中放置，v0.6 的 CropPdf 按此反转置回正，
   文字位置校验误差 <1pt。竖版 X01 与加长横版 A01 均已目检确认完整、转正。
2. **图纸空间过滤（严重）**：`SelectAll` 会跨布局选中图纸空间实体，其纸面坐标被当成
   模型窗口打印（v0.5 真实图中的 45 个 PM 图签 SKIP 和 PM-19 出图即此问题——PM-19 的
   旧"成品"实为纸面坐标映射到模型空间的错页）。v0.6 按 OwnerId 只认模型空间实体。
3. **非标准比例**：1:150/250/500/1000 等改用 `SetCustomPrintScale`（实测 1:150→精确
   300×200mm、1:1000→精确 84.1×59.4mm）；删除 `1000 → StdScale1000To1`（方向相反）的
   错误映射；比例属性解析改 TryParse（异常值不再中断整批）。
4. **健壮性**：未保存图纸默认输出到"我的文档\PDF_OUT"；设备缺失/输出目录非法时明确报错；
   出图失败页 CSV 记 FAIL；合并跳过缺失页并报真实原因；负留白/纸张小于内容不再裁切；
   文件名处理 Windows 保留名（CON/NUL…）与尾部点。
5. **交互**：出图对话框提交前 EndEdit（回车不丢最后一次修改）；勾选列单击即切换；
   高 DPI 自适应；留白联动纸张预览；BP1/BPLOT 尊重 USERS3/USERS4/USERS1（与 BPLOTAUTO 一致）。
6. **其它**：加载即出图（USERS2 触发）仅 accoreconsole 生效（GUI 下 NETLOAD 不再误触发，
   需要时手动 BPLOTAUTO）；config.json 换成正规迷你 JSON 解析器；空窗判定自动模式排除
   图框自身（可见的空图框不再出"只有标题栏"的页）；剔除包含框有提示；CSV 按序号排序、
   统计按真实图幅折算；模型空间实体外接框一次性缓存（大图 O(框×实体) 热点消除）；
   调试环境变量 `BPPLOT_KEEP_ORIG=1` 保留裁切前的中间 PDF（*.orig.pdf）。

## v0.5 回顾（其中"任意尺寸纸张"经 v0.6 修正后才真正成立）

1. **① 任意尺寸纸张已达成**：非标图框打印在最小可容标准纸上（精确比例居中），再由
   PdfSharp **矢量裁切到真实图幅**——最终 PDF 页面尺寸=图框尺寸，无浪费、无变形。
   实测 900×630 非标框 → 900×630mm 页面；真实图纸按加长幅精确输出（v0.5 的旋转页
   实际有裁切错位，v0.6 已修复并复验）
2. **③ 出图对话框（BPLOT）**：列表勾选、逐框改比例（纸张预览联动）、逐框改文件名、
   黑白/留白/合成单PDF 开关、输出目录浏览
3. **空窗跳过阈值修正**：窗口内可见实体=0 才跳过（稀疏图框不再误杀）
4. **注意（本机实测）**：accoreconsole 不带 `/i` 的空白图模式已失效（报错53）——
   无头脚本必须 `/i` 指定一个 DWG（可用一个空白模板图）；脚本结尾用 `_.quit _y`
   ——**该行为仅 accoreconsole 成立**（日志可见"真要放弃修改? <N>"确认提示，_y 放弃）；
   完整版 acad 里 `_.quit _y` 是"保存并关闭"，照抄会把 USERS 变量和测试几何存进图纸！

## v0.4 新增

1. **合成单 PDF（BPLOTMERGE / BPPLOT_MERGE=1）**：逐页出图后由 PdfSharp 合成单文件，
   **书签=图号_图名**（MSteel 只能文件名+序号），PDF 元数据 Title=图纸文件名
2. **空窗自动跳过**：图框窗口内无可见内容（图层冻结/关闭、块内部全隐藏）时标记 SKIP 不出空页
3. **布局批量出图（BPL）**：逐布局按其自身页面设置出 PDF
4. **DWG 拆分（BPSPLIT）**：手选图框，逐框 WBLOCK 成独立 DWG（图幅相对坐标归零）
5. **多段线伪框过滤**：`minPolylineSide`（默认 15000 图形单位）过滤小矩形误判

## 真实图纸验证（某超高层办公楼弱电施工图，约 25 页/7 万实体级真实工程，天正 T20 V8 环境，v0.6 复测）

| 指标 | MSteel | BPPlot v0.6 |
|---|---|---|
| 出图数 | 24 页 | **24 页**（模型空间图框全识别；45 个 PM 图签在图纸空间，不再误当模型窗口处理）|
| 纸张 | 强制 A0/A1（加长图被缩放）| **B0/B1 原尺寸精确 1:100**（加长图幅无变形）|
| 竖版/旋转页 | — | **v0.6 起内容转正、无裁切**（X01 目检确认）|
| 合成书签 | 文件名+序号 | **图号_图名** |
| 空页 | 无 | 无 |

注：v0.5 曾记"45 张 PM 图签识别并出图（可选）"——实测这批图签在**图纸空间**，
其旧输出（含 PM-19）是纸面坐标误映射到模型空间的错页，v0.6 已按模型空间过滤剔除；
如需出布局/图纸空间图请用 `BPL`。

关键发现：天正图框实际是标准图块（含 ATTDEF 属性：图号/图名/比例/版本号/日期），
BPTEACH 学习一次块名即可自动识别；动态块匿名 *U 名已正确解析为原始块名。
另一实测发现：DWG To PDF.pc3 的 ISO 纸型名为**竖版命名**（A1=594x841、B0=1000x1414），
旋转页内容在名义纸上转置放置（v0.6 CropPdf 据此标定）。

## 命令总表

| 命令 | 用途 |
|---|---|
| `BPLOT` | 交互式批量：框选任意实体 → 比例 → 目录 |
| `BPLOTAUTO` | 全自动（控制台加载触发：USERS2 非空即出图）|
| `BPLOTMERGE` | 批量出图并合成单 PDF |
| `BP1` | 单张快打 |
| `BPPREVIEW` | 单张打印预览（界面模式；回车=自动取最大图框）|
| `BPTEACH` | 图框学习（块名+属性标签 → config.json）|
| `BPREV` | 批量改版次/日期 |
| `BPL` | 布局批量出图 |
| `BPSPLIT` | 按图框拆分 DWG |
| `BPHELP` / `BPABOUT` | 打开在线说明 / 关于（菜单栏同款）|

参数：`USERS1`=比例、`USERS2`=输出目录、`USERS3=0`关黑白、`USERS4`=留白mm、
环境变量 `BPPLOT_MERGE=1`=合并模式（控制台用，GUI 直接用 BPLOTMERGE）

## v0.3 新增（日常出图能力补齐）

1. **图框学习（BPTEACH）**：手选一个真实图框块，自动记录块名 + 属性标签映射到
   `%APPDATA%\BPPlot\config.json`（开放 UTF-8 JSON），BPLOTAUTO 即可自动识别该类图框
2. **文件名规则模板**：`{图号}_{图名}_{日期}` 等占位符自由组合；无图号图名的框自动回退 Sheet 序号
3. **图纸目录 CSV + 纸张统计**：批打完成自动导出 `图纸目录.csv`（序号/文件名/图号/图名/比例/纸张），
   并按 A 系折算 A4 当量合计（送印估算用）
4. **批量改图框信息（BPREV）**：选中一批图框，版次自动 +1、日期改今天（可自定义），单次撤销
5. **单张快打（BP1）**：默认参数（USERS1/USERS2），选完即出
6. **留白参数（USERS4，单位 mm）**：正值=放大纸张保持比例（自动选含留白的最小纸，B 系也会参与，
   如 A1+3mm → B1）；负值=固定纸张外扩图面微调比例
7. 选纸算法实际支持 **ISO A/B 系全部毫米纸**（含 full_bleed），自动选面积最小者（最省纸）

## 已验证（AutoCAD 2023 + accoreconsole 无头实测，6 框混合场景）

- A0/A1/A2 标准框 1:100 → full_bleed 纸张、精确比例、自动横竖旋转
- 溢出框（841.5×594.2）→ 自动改"布满"防裁切，并日志提示
- 带属性图框（SCALE=1:50）→ 逐框读取比例，选包含内容的最小纸张（含旋转候选）
- **黑白 monochrome.ctb 已生效**（PDF 内容流验证：白色线映射为黑色输出）
- 自动剔除重复/包含图框；同名文件自动加序号防覆盖（限单次运行内，跨运行同名会覆盖旧文件）

## v0.2 相对 v0.1 的改进

1. **手选模式（BPLOT）放开为任意实体**——天正图框等自定义对象也能手选打印（取外接框）
2. **逐框比例**：图框块属性含"比例/SCALE"标签时（1:100 / 1：100 / 1/100 / 100 均可）逐框生效，否则用全局
3. **黑白打印默认开**：自动套 monochrome.ctb；脚本里 `(setvar "users3" "0")` 可关
4. **防裁切**：纸张比图框略小（容差内）时自动改布满并提示，不再裁边
5. **文件名去重**：同名自动加 _2、_3
6. **动态块识别修正**：取原始块名而非匿名 *U 名
7. 自动模式下闭合多段线增加**近似矩形校验**（面积/外接框>0.95），排除圆和弧形轮廓
8. BACKGROUNDPLOT 修改后恢复原值；魔数集中为常量

## 文件

| 文件 | 说明 |
|---|---|
| `src/BPPlot.cs` | 主要源码（C# 5 语法） |
| `src/BpForm.cs` | 出图对话框（BPLOT） |
| `src/BpDiag.cs` | 图纸结构诊断工具（只读） |
| `build.cmd` | 编译脚本（用系统自带 csc，无需 Visual Studio） |
| `BPPlot.dll` | 编译产物，NETLOAD 即用 |
| `PdfSharp.dll` | **必须与 BPPlot.dll 同目录部署**（裁切/合并用；libs/ 内是编译引用副本） |
| `test/` | accoreconsole 无头测试脚本与样例 |

## 编译

```
cmd /c build.cmd
```
依赖：AutoCAD 安装目录的 acmgd / acdbmgd / accoremgd / AcCui / Autodesk.AutoCAD.Interop(.Common)
共 6 个 DLL（均为 AutoCAD 自带，路径写在 build.cmd 里，换机器改 `ACAD=`）。
目标 .NET Framework 4.x，适用 AutoCAD 2021–2024。

## 部署

运行时需要两个文件在**同一目录**：`BPPlot.dll` + `PdfSharp.dll`（缺 PdfSharp 时裁切/
合并静默降级并在日志提示）。图纸目录 `config.json` 在 `%APPDATA%\BPPlot\`。

## 使用

### 方式一：AutoCAD 界面（日常）
```
NETLOAD → 选 BPPlot.dll        ← 菜单栏自动出现「BP-批量打印」（或加入启动套件一劳永逸）
BP-批量打印 菜单 → 出图对话框 / 全自动 / 单张预览 / 合并PDF / 布局 / 拆分 / 学习 ...
BPLOT       交互式批量：框选任意实体(天正图框也可) → 比例(默认100) → 输出目录
BPLOTAUTO   全自动：自动识别全部图框；比例=USERS1、目录=USERS2、USERS3=0 关黑白、留白=USERS4
BPPREVIEW   单张打印预览：选图框（回车=自动取最大）→ 预览窗口（ESC 关闭）
BP1         单张快打：默认参数，选完即出
BPTEACH     图框学习：选一个图框块，记录块名+属性标签映射（存开放 JSON）
BPREV       批量改图框信息：版次自动+1 / 日期改今天（选择后两项均可自定义）

配置文件：`%APPDATA%\BPPlot\config.json`（学习结果 + 文件名模板，UTF-8，可直接手编）
```
菜单栏没出现「BP-批量打印」时：命令行执行 `MENUBAR` 输入 `1` 显示菜单栏（功能区界面默认隐藏）。
要每次启动自动加载：`_APPLOAD` → 启动套件 → 添加 BPPlot.dll（连同 PdfSharp.dll 同目录）。
**注意**：USERS2 触发的"加载即出图"**仅在 accoreconsole 无头模式生效**（GUI 里 USERS2
是通用变量，其他插件可能正在用，NETLOAD 误触发整图批打属于事故）；GUI 下请手动执行
BPLOTAUTO / BPLOT，执行后 USERS2 自动清空。

### 方式二：无头批处理（整目录批量，推荐用于出图）
`run.bat 思路`：
```
set DWG=图纸.dwg
set OUT=D:\出图
"D:\Program Files\AutoCAD 2023\AutoCAD 2023\accoreconsole.exe" ^
  /i "%DWG%" /s "批打.scr"
```
`批打.scr`（注意：脚本路径必须是绝对路径）：
```lisp
(setvar "secureload" 0)
(setvar "filedia" 0)
(setvar "users1" "100")                      ; 比例 1:100
(setvar "users2" "D:/出图")                   ; 输出目录（正斜杠）
(command "_.netload" "C:/路径/BPPlot.dll")    ; 加载即出图（见下）
_.quit _y                                    ; accoreconsole: _y=放弃修改；勿在完整版 acad 里照抄
```

## 图框识别规则

- **图块**（含动态块，取原始块名）：名字含 `图框 / TK / FRAME / TITLEBLOCK / 标题栏`（BPLOTAUTO 自动模式过滤；BPLOT 手选时不过滤，选什么是什么）
- **闭合多段线** ≥4 顶点且近似矩形（面积/外接框>0.95，自动模式）；手选模式任意实体皆可
- **天正图框**：自动识别不保证（自定义对象非普通块）；用 BPLOT 手选可直接打印
- 块属性含"比例/SCALE"标签时逐框采用（1:50、100 等格式均可），否则用全局比例
- 自动剔除重复和被包含的框（与 MSteel 行为一致）

## 出图逻辑

- 设备 `DWG To PDF.pc3`，窗口打印，detached PlotSettings + `PlotInfo.OverrideSettings`（官方范式）
- 选纸：能容纳的最小毫米纸张，等面积时优待 full_bleed（无边距精确出图），含横竖旋转判断
- 精确比例 1:n（标准比例枚举 + StdScale 双精度）；图框超过最大纸张时退化为布满纸张并在日志提示
- 文件名优先取图块属性（图号→图名→SHEET/标题），非法字符自动替换

## 踩坑记录（对本机 AutoCAD 2023 实测）

1. **accoreconsole `/s` 只认绝对路径**（相对路径报"未找到文件"后空转）
2. 控制台里 `(command "netload")` 能加载程序集并跑 Initialize，**但 CommandMethod 命令不注册**（完整版 acad 无此问题）；所以自动化走"USERS2 触发 + 加载即出图"路线
3. `Plotting` 命名空间在本版叫 `Autodesk.AutoCAD.PlottingServices`；PlotSettings 属性只读，全部走 `PlotSettingsValidator.SetXxx()`
4. **必须先 `SetPlotWindowArea` 再 `SetPlotType(Window)`**，否则 eInvalidInput
5. 校验器 `MatchingPolicy.MatchEnabled` 会覆盖活布局上的显式选纸 → 用 detached OverrideSettings 后则完全尊重显式选纸
6. **DWG To PDF.pc3 的 ISO 纸型名为竖版命名**（A1=594x841、A2=420x594、B0=1000x1414；B1 例外为 1000x707）。**旋转页（PlotRotation 90°）的内容在名义纸页面上是转置放置的**（图面 X 轴 → 纸面 Y 向下），裁切必须反转置回正，否则竖版图全部裁坏（v0.5 的教训，v0.6 已按实测标定，`BPPLOT_KEEP_ORIG=1` 可复现中间形态）
7. `Editor.SelectAll` 等价 `(ssget "_X")`，**跨布局包括图纸空间**；按图框收集必须再按 OwnerId 过滤模型空间（v0.5 的 PM 图签误识别即此）
8. 非标准打印比例走 `SetUseStandardScale(false) + SetCustomPrintScale(new CustomScale(1, n))`；`StdScaleType` 枚举没有 1:1000（只有 1000:1），勿按分母硬套枚举
9. accoreconsole 的 `_.quit` 二次确认与完整版 acad 语义相反（控制台 `_y`=放弃修改；图形界面 `_y`=保存并关闭），脚本务必分环境对待

## 路线图

**已完成**（详见上方版本历史）：菜单栏 BP-批量打印（v0.7）｜单张打印预览 BPPREVIEW（v0.7）｜出图对话框 BPLOT（v0.5）｜合成单 PDF+书签（v0.4）｜布局批量出图 BPL（v0.4）｜DWG 拆分 BPSPLIT（v0.4）｜图框学习 BPTEACH+天正 T20 块名兼容（v0.3）｜文件名模板（v0.3）｜黑白打印 monochrome.ctb（v0.2）

**待办**：
1. 多 DWG 目录遍历——第一步补一个 `for %%f in (*.dwg)` 循环调 accoreconsole 的批处理脚本（含失败清单汇总）；accoreconsole 多进程并行按需评估（并行时每进程需独立 DWG 与输出目录，license 占用翻倍）
2. 出图预览确认——**单张预览引擎路线已实测打通**（v0.7 BPPREVIEW：复用 BuildPage + `PlotFactory.CreatePreviewEngine`，预览阻塞等待人工关闭）。剩余：对话框内嵌预览入口、批量预览、「预览→确认→落盘」流程（预览引擎按正式参数渲染，确认后可直接落盘避免二次出图）。仅界面模式有效，无头批打以"试跑样本→再全量"替代

**不做**：实体打印机纸张输出——插件定位就是 PDF 出图，设备固定 `DWG To PDF.pc3`，不开放设备选择。

## 兼容性

AutoCAD 2021–2024（.NET Framework）。2025+ 需换 Roslyn/net8.0 编译，代码逻辑不变。
