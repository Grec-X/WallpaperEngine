# 手机壁纸引擎（Wallpaper Engine for Android）

将任意本地视频设为 Android 动态壁纸，无缝循环播放。C# / .NET 9 for Android + Media3 ExoPlayer。

> 🤖 本项目由 **Vibe Coding（AI 辅助编程）** 完成——人类负责产品决策、体验验收与真实场景测试，AI（ZCode × GLM）负责架构设计、编码与调试攻坚。全程协作记录见 `docs/日志/`。

## 功能

- 🎬 **视频动态壁纸**：系统级 Live Wallpaper 服务，无缝循环播放（ExoPlayer 原生循环 + 保比例裁剪铺满）
- 🖥 **桌面与锁屏分别设置**：两个位置各有独立的视频与默认壁纸。可以桌面放一个视频、锁屏放另一个，也可以一边视频一边图片
- 🖼 **视频与图片**：视频循环播放；图片按静态壁纸提交给系统
- ⚙ **播放设置**：音量、填充模式、循环区间，桌面与锁屏分别保存，切换编辑对象即可调整
- 🕘 **壁纸库**（右上角 ☰）：
  - **历史与收藏**：各容纳 25 个并显示当前数量，内容 MD5 去重，超限自动淘汰文件
  - **桌面默认 / 锁屏默认**：长按设为各自位置的默认壁纸，缩略图标注类型；恢复默认时未设置则还原系统壁纸
  - **收藏与删除**：长按标记收藏（豁免自动清理）或删除（连同文件与缩略图）
- ⚡ **生命周期**：灭屏 / 锁屏 / 被遮挡时自动暂停，回到桌面自动恢复；重启自恢复；播放异常时状态条显示「播放异常」并自动回退
- 🎨 **现代深色 UI**：双引擎状态条、双壁纸卡片（直读系统实际画面）、右侧设置抽屉（右滑关闭）、内置使用说明页
- 📚 **工程文档**：技术方案 + 四阶段开发日志，记录每一轮决策、bug 根因与实测数据
- 📦 **内置默认素材**：首次安装即有默认壁纸

## 兼容性

- 双 ROM 真机验证：OPPO Reno6 Pro（ColorOS 13 / Android 13）、iQOO Neo 9（OriginOS 6 / Android 16）
- Android 15+ 强制 edge-to-edge 已适配（系统栏 insets 处理）
- **锁屏独立动态壁纸依赖 Android 14+ 框架能力**：OriginOS 6 实测可用；Android 13 及更早系统上锁屏跟随桌面，应用会提示
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
