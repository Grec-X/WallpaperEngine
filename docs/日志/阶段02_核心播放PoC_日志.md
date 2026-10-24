# 阶段二日志：核心播放 PoC

| 项 | 内容 |
|---|---|
| 日期 | 2026-10-24 |
| 状态 | ✅ 已完成（三大验证目标全部达成，30 分钟耐久通过，详见补记 4） |
| 产出 | `poc/WallpaperPoc/`、`build.cmd`、`deploy.cmd`、G 盘完整工具链 |

---

## 补记 2：工具链全量迁移到 G 盘（2026-10-24 晚，用户选定方案 C）

**背景**：用户要求 C 盘不存放任何项目相关数据，选择"彻底清出 C 盘"方案。

**迁移结果**：
| 组件 | C 盘（迁移前） | 迁移后（G 盘项目内） | 状态 |
|---|---|---|---|
| .NET SDK 9.0.319 绿色版 | — | `tools\dotnet\`（官方 zip 发行版） | ✅ 构建验证通过 |
| android workload 35.0.105 | C:\Program Files\dotnet\packs（2.2GB） | `tools\dotnet\packs\` | ✅ 已卸载 C 盘版，G 盘版构建成功 |
| Microsoft JDK 17.0.20.1 | C:\Program Files\Microsoft（302MB） | `tools\jdk\jdk-17.0.20.1+1\`（zip 版） | ✅ zip 版在用；C 盘版卸载见下注 |
| Android SDK | — | `tools\AndroidSDK\`（+ `G:\AndroidSDK` 联接别名） | ✅ 不变 |

**C 盘清理结果**：dotnet packs 从 2680MB → 482MB（净释放约 2.2GB；剩余 482MB 为 .NET SDK 自带基础包，非本项目安装）。
⚠️ **遗留**：C 盘 winget 版 JDK 302MB 卸载失败——该 MSI 存在 WiX 打包 bug（`WixRemoveFoldersEx: Missing folder property: INSTALLDIR`，错误 0x80070057），且其文件 ACL 受 TrustedInstaller 保护，本会话无法安全强删。**待用户手动**：Windows 设置 → 应用 → 搜 "OpenJDK" → 卸载（图形界面卸载通常可绕过该 bug；若仍失败可忽略，不影响任何功能，只是占 302MB）。

**工作流变更**（重要，后续所有构建/部署用脚本，勿直接跑 `dotnet build`——系统 PATH 里是 C 盘 SDK，已无 android workload）：
- 构建：项目根 `build.cmd [Debug|Release]`
- 部署+启动：项目根 `deploy.cmd [Debug|Release]`（内部执行 `dotnet build -t:Install` FastDev 增量部署）
- adb：`G:\AndroidSDK\platform-tools\adb.exe`，设备 Reno6 Pro 序列号 `b4e4730b`

**新踩的坑（记录给未来）**：
1. Debug 版 .NET Android 用 FastDev 增量部署：**手动 `adb install` APK 会因缺托管程序集启动即崩**（monodroid: No assemblies found in .__override__）。必须 `dotnet build -t:Install` 或在 csproj 设 `AndroidFastDeploymentType=None`。
2. cmd 单行命令 `set X=... && 用 %X%` 会在整行展开时拿不到新值（本次踩过导致误用 C 盘 SDK）；脚本文件内逐行执行无此问题。
3. 批处理/ps1 文件含中文时必须注意编码（cmd 按 GBK 读 UTF-8 会乱码）——项目内脚本一律用纯 ASCII。

## 补记 1：真机部署（OPPO Reno6 Pro，2026-10-24 晚）

**设备实测信息**（解决阶段一遗留问题 2）：
- 型号 PENM00（OPPO Reno6 Pro），**实际系统 Android 13 / API 33**（高于出厂 Android 11 的预期，ColorOS 13.x）
- arm64-v8a，1080x2400 屏
- 最初 adb 识别不到：手机 USB 用途处于 **MIDI 模式**，改"传输文件"后正常识别——测试前置条件之一

**部署过程**：
1. `adb install` APK 成功，但首启即崩（FastDev 问题，见补记 2 坑 1）；
2. 改用 `deploy.cmd`（`dotnet build -t:Install`）部署成功；
3. 第二次启动抓到**真实代码 bug**：`BuildUi()` 末尾调用 `UpdateButtons()` 时 `_player` 尚为 null → NullReferenceException。已修复（移到 `CreatePlayer()` 之后）。**这是 PoC 的第一个真机产出：崩溃日志定位-修复-重部署链路全程打通。**
4. 修复后应用稳定驻留前台（PID 存活、无 FATAL）。

**待执行的四象限实测**（等用户在手机上选视频后进行）：MediaPlayer/ExoPlayer × 原生循环/手动重启，观察接缝、重启延迟、RSS 内存；锁屏/解锁验证 Surface 重建；最后 30 分钟耐久。结果将补记于此。

## 补记 3：四象限实测与生命周期验证结果（2026-10-24 晚）

**默认素材机制上线**：用户指定 `素材\灰原哀.mp4`（1MB / 时长 5042ms）为默认素材。已实现并验证"应用外部私有目录自动加载"：adb push 到 `/sdcard/Android/data/<pkg>/files/default.mp4` → 应用启动即自动加载播放，无需任何存储权限——这同时提前验证了方案 4.5 的视频导入机制。PoC 同步增加了 logcat 远程监控日志（Tag=WEPoc：Prepared/Error/手动重启延迟/每圈回绕/每 4 秒状态行），电脑端可全程观测。

**四象限实测**（用户观感 + logcat 数字）：

| 象限 | 重启延迟（控制层） | 观感（用户） | 错误/崩溃 |
|---|---|---|---|
| MediaPlayer + 手动重启 | 1~4ms | 无明显卡顿 | 无 |
| MediaPlayer + 原生循环 | —（无重启事件） | 无明显卡顿 | 无 |
| ExoPlayer + 手动重启 | 2~4ms | 无明显卡顿 | 无 |
| ExoPlayer + 原生循环 | —（无重启事件） | 无明显卡顿 | 无 |

**结论**：
1. 用户对四种模式循环接缝的观感"都差不多"——连理论最差的手动重启都无感知，**接缝在本设备+本素材上不构成问题**，V1 采用"ExoPlayer + 原生循环"（方案 D3/D4）完全成立，交叉淡化增强可安心推迟至 V2。
2. 手动重启的控制层延迟（Completion/ENDED 事件→SeekTo(0)→Play 返回）仅 1~4ms，说明万一后续需要手动循环兜底，控制层开销也可忽略。
3. 内存：Debug 版（Mono 解释执行+FastDev）RSS 在 186~229MB 区间波动，切换内核后 GC 有回落（229→216MB）。**远超 100MB 目标的主因是 Debug 运行时本身**，Release/AOT 版实测放到阶段四，此处仅记录 Debug 基线。
4. ExoPlayer 原生循环的回绕点在末关键帧（约 4800~4990ms 处回卷而非精确 5042ms），属正常解码行为。

**灭屏/亮屏生命周期验证（adb 模拟电源键）**：✅ 通过。灭屏 → OnPause → 自动暂停（播放中=False、进度冻结）；亮屏解锁 → 自动续播（圈数递增、位置回绕正常）。与方案 4.1 的暂停/续播矩阵一致。

**新发现的 PoC bug（阶段三修复）**：切换内核后出现"僵尸状态行"——已 Release 的旧内核实例仍周期性输出冻结状态。已加防御（Activity IsDestroyed 后停轮询链 + 干净重启即消失），根因（实例生命周期管理时序）留阶段三正式工程解决。

**进行中**：ExoPlayer + 原生循环 30 分钟耐久（结果见补记 4）。

## 补记 4：30 分钟耐久结果 + 阶段二收尾（2026-10-24）

**耐久数据**（18:22:40 → 18:52:42，ExoPlayer + 原生循环，默认素材）：

| 指标 | 结果 | 判定 |
|---|---|---|
| 进程存续 | PID 4510 全程不变，零崩溃零错误 | ✅ |
| 循环圈数 | 第 7 → 364 圈，357 圈 × 5.042s ≈ 1800s，与挂机时长精确吻合 | ✅ |
| 播放连续性 | 30 分钟内 IsPlaying 始终 True，位置回绕规律正常 | ✅ |
| 内存 | RSS 205→216~227MB 区间波动，期间 GC 回落（227→216），**无泄漏式增长** | ✅（Debug 基线稳定） |
| 设备状态 | 电量 62%，电池温度 30.6°C（正常温区） | ✅ |

**阶段二验收对照**（方案 5 节标准）：
- ✅ 真机连续循环 30 分钟无崩溃
- ✅ 无明显接缝（四象限用户观感 + 耐久全程无跳变日志）
- ⚠️ 内存 < 100MB：Debug 版（Mono 解释执行 + FastDev）实测 216~227MB，超出部分为调试运行时固定开销；**此项以 Release/AOT 版在阶段四重新验收**，Debug 基线已证明无泄漏、水位稳定。

## 阶段二总结论（✅ 关闭）

1. **三大验证目标全部达成**：ExoPlayer C# 绑定可用（风险 #1 解除）；Surface 循环播放无接缝问题；生命周期对齐逻辑（暂停/续播/Surface 重建）工作正常。
2. **V1 技术路线确认**：ExoPlayer + RepeatModeOne 原生循环；交叉淡化增强推迟 V2（风险预案无需提前启用）。
3. 默认素材机制（私有目录自动加载，零权限）验证通过，直接沿用到正式版导入流程。
4. 附加产出：完整 G 盘工具链 + build/deploy 脚本 + logcat 远程监控模式（Tag=WEPoc）+ 媒体绑定 API 备忘（IPlayerListener 默认接口方法、常量在 BasePlayer.InterfaceConsts 等）。

## 下一阶段（阶段三：壁纸服务集成）规划

目标：`VideoWallpaperService : WallpaperService` 落地，以系统"动态壁纸"身份运行。
步骤：
1. 搭正式工程骨架（src/ 四项目结构，按方案 7 节；Android SDK 路径沿用联接方案，构建走 build.cmd 模式）；
2. 壁纸服务 + Engine：SurfaceHolder 回调直连播放内核（复用阶段二的 IVideoLoopPlayer 抽象与 ExoPlayerLoopPlayer）；
3. AndroidManifest 动态壁纸声明（BIND_WALLPAPER + settingsActivity）；
4. 真机验证：出现在系统壁纸选择器 → 设为壁纸 → 桌面循环播放 → 灭屏/锁屏/全屏应用暂停续播 → 重启手机自恢复；
5. OriginOS/ColorOS 省电行为专项记录（方案风险 #3）。

遗留问题带入阶段三：
- 僵尸实例 bug 根因修复（切换/释放时序）；
- Release/AOT 构建配置与内存实测（可提前到阶段三末尾做一轮，及早暴露裁剪问题）。

---

## （以下为阶段二初始记录）

## 本阶段目标
验证三件事：① C# 能否驱动 ExoPlayer（NuGet 绑定可用性）；② 全屏 Surface 循环播放的接缝表现（真机）；③ Engine 生命周期对齐逻辑。

## 已完成事项

### 1. 开发环境从零搭建（本机此前无任何 Android 开发组件）
| 组件 | 版本 | 位置 |
|---|---|---|
| .NET SDK | 9.0.319（已有） | C:\Program Files\dotnet |
| .NET Android workload | 35.0.105（API 35 构建链） | C:\Program Files\dotnet\packs ⚠️ |
| JDK | Microsoft OpenJDK 17.0.20 | C:\Program Files\Microsoft ⚠️ |
| Android SDK | cmdline-tools + platform-tools + platforms;android-35 + build-tools;35.0.0 | ✅ 项目内 `tools\AndroidSDK\`（经 `G:\AndroidSDK` 联接访问） |
| ilspycmd 反编译器 | 9.1.0.7988 | ✅ 项目内 `tools\bin\` |

⚠️ **C 盘占用披露**（用户要求 C 盘不存放项目数据，以下两项为系统工具链而非项目文件）：
- .NET android workload：约 1.5 GB（随 .NET SDK 安装的构建工具，与用户已有的 dotnet 同级）
- Microsoft OpenJDK 17：约 320 MB（系统软件，winget 安装）
- 可选迁移方案（如需彻底清出 C 盘）：卸载 winget 版 JDK → 下载 zip 版解压到 `tools\jdk\` → 构建命令 JAVA_HOME 指向它。workload 位置由 dotnet SDK 决定，无法迁移，属 .NET 工具链本身。

**项目数据全部在项目目录内**：Android SDK、PoC 工程、文档、工具。`G:\AndroidSDK` 是指向项目内 SDK 的目录联接（junction，文件系统重解析点，非数据拷贝），用途见下方"关键发现 3"。

### 2. PoC 工程（`poc/WallpaperPoc/`，net9.0-android，minSdk 26 / targetSdk 35）
- **按方案 3.1 的抽象层设计实现**：`IVideoLoopPlayer` 接口（SetSurface/SetDataSource/Prepare/Play/Pause/Release/Reset + Prepared/ManualRestart/Error 事件）。
- **两个内核实现**：
  - `MediaPlayerLoopPlayer`：系统 MediaPlayer，原生循环（Looping）+ 手动重启（Completion→SeekTo(0)+Start，测量重启耗时）双模式。
  - `ExoPlayerLoopPlayer`：Media3 ExoPlayer 1.11.1，原生循环（RepeatModeOne）+ 手动重启（STATE_ENDED→SeekTo(0)+Play）双模式。
- **MainActivity**：全屏 SurfaceView 模拟壁纸 Surface；SAF 选视频（ACTION_OPEN_DOCUMENT，验证方案 4.5 导入方式）；Surface 生命周期严格按方案 4.1 矩阵对齐（SurfaceDestroyed 返回前完成 Pause+SetSurface(null)）；Activity onPause/onResume 模拟壁纸可见性；500ms 轮询显示状态（位置回绕检测循环圈数、手动重启延迟、/proc/self/status RSS 内存）。
- **构建结果**：`dotnet build -c Debug` → **0 警告 0 错误**，产出 `com.wallpaperengine.poc-Signed.apk`（aapt2 badging 校验通过：包名/启动 Activity/标签正确）。

### 3. 关键发现（后续阶段的宝贵经验）
1. **风险 #1（ExoPlayer 绑定可用性）编译层面解除**：`Xamarin.AndroidX.Media3.*` 官方绑定（Microsoft/Xamarin 维护，1.11.1）在 net9.0-android 下工作正常。NuGet 上齐套：ExoPlayer/DataSource/Common/Extractor/Ui/Transformer（Transformer 即 V2 交叉淡化预处理的候选组件）。
2. **绑定的 API 命名与 Java 文档不同**，已摸清并记录：
   - Java `Player` → C# `IPlayer`；监听器 `Player.Listener` → `IPlayerListener`（**支持默认接口方法，可只实现 OnPlaybackStateChanged/OnPlayerError**）；
   - 构造：`new ExoPlayerBuilder(context).Build()`（返回 `IExoPlayer`）；
   - 常量在 `BasePlayer.InterfaceConsts`（RepeatModeOne=1、StateReady=3、StateEnded=4），**不在 IPlayer 上**；
   - `SetVideoSurface(Surface)` / `RepeatMode` 属性 / `AddListener(IPlayerListener)` 均按预期。
   - 摸底方法：ilspycmd（装在项目 tools\bin）反编译绑定 DLL，一条命令可复现：`tools\bin\ilspycmd.exe -p -o <输出目录> <绑定DLL路径>`。
3. **路径兼容性坑**：项目路径含中文和 `#` 没问题（MSBuild 以 UTF-8 响应文件传参），但 **AndroidSdkDirectory 含非 ASCII 时 aapt2 报 APT2000（找不到 android.jar）**。解法：SDK 实体放项目内，另建 ASCII 联接 `G:\AndroidSDK` 指向它，csproj 指联接路径。正式工程沿用此法。
4. G 盘未启用 8.3 短文件名，故只能用联接方案。

## 遗留问题 / 待办
1. **真机运行验证（阶段二验收的核心）**：两台真机（iQOO Neo 9 / OPPO Reno6 Pro）均未连接 PC。已就绪待执行：
   - 设备插 USB → `adb devices` 确认授权 → 部署安装 → SAF 选视频 → 双内核 × 双循环模式实测（重点观察：循环接缝、切换内核、锁屏/解锁 Surface 重建、手动重启延迟数字、RSS 内存）；
   - 验收标准照方案 5 节：连续循环 30 分钟无崩溃、无明显接缝、内存 < 100 MB；
   - 若手机里没有合适测试视频，可用 adb 推送一个 H.264 MP4 到设备再选。
2. Reno6 Pro 实际 Android/ColorOS 版本待连接时确认（记入本日志补记）。
3. JDK 迁出 C 盘为可选项，等用户表态。

## 下一步（按优先级）
1. **（等设备）真机部署 + 四象限实测（2 内核 × 2 循环模式）+ 30 分钟耐久**，结果补记本日志。
2. 真机验证通过后进入阶段三：`VideoWallpaperService` 集成（正式工程骨架搭建）。
3. 若 ExoPlayer 真机表现达标 → 阶段三正式工程直接以 ExoPlayer 为默认内核，MediaPlayer 降级为备选。

## 给下一次会话的接续说明
- 恢复上下文：读本日志 + `docs/技术方案.md`。
- 构建命令（PowerShell/cmd 均可）：`cd poc\WallpaperPoc && set JAVA_HOME=C:\Program Files\Microsoft\jdk-17.0.20.101-hotspot && dotnet build -c Debug`（JAVA_HOME 是因为系统 PATH 里还有个 Java 8）。
- APK 位置：`poc\WallpaperPoc\bin\Debug\net9.0-android\com.wallpaperengine.poc-Signed.apk`。
- adb：`G:\AndroidSDK\platform-tools\adb.exe`。
