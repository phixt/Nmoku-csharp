# 预开始页原生融入设计（以 Godot/C# 为基底）

> 状态：设计定案待人类确认 · 2026-10-01 · 分支 `prestart-native`
> 背景：HTML 原型（tools/title-prestart-design.html）三轮反馈后，动效在浏览器里调不出满意效果
> （CSS 手段到顶），且 HTML→Godot 等于整套机制翻译、成本高还二次失真。转向：**原型只作视觉稿
> 参考，功能以游戏原生栈直接实现**。

## 核心：复用真实 3D 棋盘（本设计的支点）

预开始页最难的"可交互小立体棋盘"（拖拽旋转/悬停蓝球/辅助线）在游戏里**已经是现成且经人类验收
的真实机制**，HTML 里那段 canvas + 自创辅助线整体作废：

- **Board3DView + CameraRig**（M2/M3）：无级 Orbit 旋转 + 指数阻尼（帧率无关）——预开始页的
  "默认自转、拖拽、惯性、弹性"就是相机参数，不是要新写的动画系统；
- **AuxScan + 3D 辅助线 + 悬停蓝球**（M3，人类验收版）：辅助线真实机制**无需移植**——演示直接用
  游戏本体机制；若观感与原图标语义仍有出入，回到 migration-plan §10 对齐源实现（复核点保留）；
- **摆子**：LevelCatalog/Configure 预置子机制（M4）新增一个"展示档"——黑 (2,0,1)、白 (2,1,0)、
  蓝球悬停位 (1,1,1)（Core 侧坐标转换注意层序口径）；
- **玩具落子（远期）**：Board 4×4×3 + winLength 5（无法成五），走 Core 配置 + GameManager 通用
  路径，不新写机制。

## 效果 → Godot 映射表

| HTML 原型 | Godot 原生 |
|---|---|
| N 矢量字标（SVG） | `Control._Draw` 自绘多边形（色值走 Palette，任意分辨率清晰；推荐）；备选 SVG 高分辨率导入 |
| 悬停 glow | canvas_item shader 外发光，或预烘焙光晕纹理叠加 |
| 视差 | InputEventMouseMotion → 目标偏移，_Process 阻尼插值（CameraRig 同款手感） |
| 音乐响应 | `AudioServer.GetBusPeakVolumeLeftDb(Bgm)` → 线性值 → shader 参数/呼吸缩放；接**真 BGM 总线** |
| 音量图标四态 | 自绘 Control 四态 + 点击循环，与 AudioManager 音量/静音同步（M 键/设置页共用状态） |
| 45°+180° 翻转 | Tween：pivot 居中 rotation 0→45°，scale.x 1→0→-1（读作绕竖轴翻面），落位后交叉淡入**真实 3D 棋盘**（已在自转）；参数全部常量化 |
| 字体分组 | 全局 LXGW（现状）；按钮 SystemFont("SimSun") 18 号（Godot 4 原生系统字体，目标平台仅 Windows）；多语言按 locale 映射（宋体↔Times New Roman/日语等默认 LXGW）进 Theme/I18n 层 |
| 亮色底/图标 | Palette.Light 现成；图标亮色化 = make-icon.py 按参考图重绘（独立小任务，不入本批） |

## 场景接入（Core 零改动）

- SceneRouter 加 L0 路由 id `pretitle`（纯壳层 RouteDef）：首帧先挂 pretitle，点击进入 → 路由转
  `title`。**GameStateMachine 七态与 §3.4 转移表不动**（状态机仍以 Title 为起点，pretitle 只是
  壳层前置屏）。
- pretitle 的 board 用独立 SubViewport（OwnWorld3D + MSAA，同 GameScreen 手法 + ViewportHost
  自绘宿主），与 game 的视口互不干扰；KeepAlive 常驻，离开后停自转省帧。
- Phase2 布局 = 已定案：棋盘左、按钮右、缩小标题左上角；按钮即 TitleScreen 现有七键迁移重排。

## 动效不满意 → 原生迭代回路

CSS 动效与引擎动效不同源，在原型里继续调参没有意义。动效参数（时长/缓动/翻转轴向/自转速度/
惯性系数/视差幅度）集中到 `PretitleMotion` 常量类，实现后 **computer use 真实窗口逐项调参走查**，
以引擎内观感为唯一准绳；翻面具体形态做 2–3 个变体（45°+180° 二段 / 单轴 90° 交替 / 纯缩放淡变）
供人类挑选。

## 分支与实施步骤

分支 `prestart-native`（自当前 master 建出），commit 前缀 `M5-pre:`，完成后合回 master 走验收：

1. N 自绘组件 + PreTitleScreen 壳 + 路由接入（静态版：先跑通路由/主题/i18n/字体分组）
2. board SubViewport + CameraRig 自转/拖拽 + 展示档摆子
3. 过渡动效 Tween + 音量组件 + 音乐响应
4. 视差/悬停打磨 + 调参走查 + headless 自测钩子

## 待人类拍板

1. N 动效形态：**2D 翻面（推荐，稳、成本低）** vs 3D 挤出网格真旋转（贵，可作变体）。
2. 演示棋盘构图：近景特写占半屏（原型的方案 D 镜像） vs 全屏背景化。
3. 玩具落子是否随第一批实现（建议后置，先出静态+旋转）。
