# 手机壁纸引擎（Wallpaper Engine for Android）

将任意本地视频设为 Android 动态壁纸，无缝循环播放。C# / .NET 9 for Android + Media3 ExoPlayer。

> 🤖 本项目由 **Vibe Coding（AI 辅助编程）** 完成——人类负责产品决策、体验验收与真实场景测试，AI（ZCode × GLM）负责架构设计、编码与调试攻坚。全程协作记录见 `docs/日志/`。

## 功能

- 🎬 **视频动态壁纸**：系统级 Live Wallpaper 服务，桌面常驻无缝循环（ExoPlayer 原生循环 + Cover 保比例裁剪铺满）
- 🖼 **混合壁纸体系**：视频与图片混搭——桌面视频循环 + 锁屏定格帧；或直接提交图片为锁屏/桌面静态壁纸（按设备能力自适应，异常明确提示）
- 🔁 **循环区间裁剪**：选定视频任意片段循环（ClippingConfiguration）
- 🔊 **音量 / 填充模式**：壁纸静音默认，Cover / Stretch 切换
- 🕘 **壁纸库**（右上角 ☰）：
  - **历史壁纸**：最多 10 条，内容 MD5 去重，超限自动淘汰清理文件
  - **收藏**：长按标记精选，豁免自动清理
  - **删除**：长按删除不需要的壁纸（连同文件与缩略图）
  - **自定义默认壁纸**：长按设为默认（角标标识）；未设默认时"恢复默认"还原系统原生壁纸
- ⚡ **生命周期**：灭屏/锁屏自动暂停、亮屏恢复、重启自恢复、播放源错误自动恢复
- 🎨 **现代深色 UI**：激活状态横幅、右侧设置抽屉（右滑关闭）、使用说明页（半透明层叠导航）、深色菜单与对话框体系
- 📖 **内置使用说明**：独立页面，分功能区介绍全部能力
- 📦 **内置默认素材**：首次安装即有默认壁纸

## 兼容性

- 双 ROM 真机验证：OPPO Reno6 Pro（ColorOS 13 / Android 13）、iQOO Neo 9（OriginOS / Android 16）
- Android 15+ 强制 edge-to-edge 已适配（系统栏 insets 处理）
- 混合壁纸能力因 ROM/框架版本而异（详见 `docs/日志/阶段04` 支持矩阵）：锁屏定格帧双机可用；"锁屏视频+桌面静态"需 Android 14+ 框架锁屏动态壁纸支持（OriginOS 6 验证可用）
- minSdk 26 / targetSdk 35

## 项目结构

```
src/
├── WallpaperEngine.Core/     配置与历史（纯 C#，JSON 持久化）
├── WallpaperEngine.Player/   播放内核（IVideoLoopPlayer 抽象 + ExoPlayer/MediaPlayer 实现）
└── WallpaperEngine.App/      管理界面 + VideoWallpaperService（壁纸服务）
poc/                          阶段二验证工程（归档）
docs/                         技术方案 + 各阶段日志（项目全程决策记录）
tools/                        开发工具链（git 忽略，见下）
素材/                         默认壁纸素材
```

## 构建

> **自备默认素材**：出于版权考虑，个人素材视频不入库。克隆后请自行放置一个 MP4 到
> `src/WallpaperEngine.App/Assets/default.mp4`（应用首次启动会将它作为默认壁纸）。
>
> 本仓库自带全量开发工具链（.NET SDK、JDK、Android SDK 约 3GB），因体积原因不入库。
> 首次构建请按 `docs/日志/阶段02_核心播放PoC_日志.md` 的环境章节重建工具链，或使用本机已装的 .NET Android 环境。

```
build.cmd [Debug|Release]     # 构建（Release 为 AOT，含 ACW 修复钩子，见下）
deploy.cmd [Debug]            # 构建 + 安装到设备 + 启动
```

- minSdk 26 / targetSdk 35，仅在 Android 真机测试（OPPO Reno6 Pro / ColorOS 13）
- 日志观测：`adb logcat -s WESvc WESvc.Engine WEApp`

## 已知上游问题与规避

`WallpaperService.Engine`（Java 内部类）的子类在 .NET Android **Release** 构建中会被 ACW 生成器产出为非法 Java 源（顶层类继承内部类），javac 失败。本项目以构建后处理 `src/WallpaperEngine.App/fix-acw.ps1`（`FixEngineAcw` MSBuild target）改写生成产物绕过，详见 `docs/日志/阶段04_管理应用与体验完善_日志.md` 补记 5。

## 性能（真机实测）

| 指标 | OPPO Reno6 Pro（ColorOS 13） | iQOO Neo 9（OriginOS / Android 16） |
|---|---|---|
| 循环接缝 | 四模式无感知差异，30 分钟 357 圈精确循环 | ✅ |
| 壁纸服务内存（Release/AOT） | PSS 77.7MB | PSS 105MB（高密度 + 高分辨率素材场景） |
| 换视频热切换 | 控制延迟 1~4ms | ✅ |
| 系统兼容 | 灭屏/锁屏/重启自恢复 ✅ | ✅（含 Android 15+ 强制 edge-to-edge 适配） |

## 关于本项目的 Vibe Coding 协作模式

本项目从零到可用产品全程由人机对话完成，未手写一行代码：

- **人类（产品负责人）**：需求定义、技术方案拍板、真机体验验收、产品决策（如砍掉与系统重复的功能）、暴露真实 bug 与体验反馈；
- **AI（ZCode × GLM）**：技术方案撰写、全部编码、构建链搭建与攻坚（含绕过 .NET Android 上游 ACW 生成器 bug）、远程设备调试（adb 观测/日志取证/内存实测）、阶段日志沉淀。

`docs/日志/` 下的四份阶段日志完整保留了每一次决策、bug 根因分析与修复过程——包括三轮迭代式的激活状态检测修复、ColorOS 双引擎机制的发现、Release 构建工具链攻坚等，可作为 Vibe Coding 工程实践的完整案例阅读。
