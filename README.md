# WorldRelic

WorldRelic 是一个独立的、离线运行的 Unity 外部世界文件兼容项目，包含 Windows PC 和 Android 两套源码工程。

## 非关联声明

**本项目与 VRChat、Anique Museum、《僕のヒーローアカデミア》及其制作方、运营方和其他权利人没有任何隶属、合作、赞助、背书或官方授权关系。** WorldRelic 不是上述任何一方的官方项目，也不是 VRChat 客户端。

本项目只是恰好兼容用户自行提供的特定版本外部文件，其对应展示为：

> アニメ「僕のヒーローアカデミア」 体験型バーチャル展示 ヒーローたちの足跡 - Anique Museum

上述名称仅用于说明兼容对象，不表示关联或授权；名称、商标及展示内容的权利归各自权利人所有。源码提供不意味着授予这些内容的使用、复制、传播或商业使用权。请自行确认外部文件来源和使用权限。

## 这份源码包包含什么

- `PC/UnityProject`：Windows x64、D3D11、键鼠／普通手柄／OpenXR VR。
- `Android/UnityProject`：Android ARM64、GLES3、IL2CPP、触摸／普通手柄／PICO OpenXR。
- 自写的运行时加载器、有限序列化适配、数据读取器、解释器、交互／抓取／移动／音频策略，以及程序生成的手模型和两个通用显示 Shader。
- 空 Bootstrap 场景、通用 TMP／XR 配置、构建脚本和 Android 原生文件选择／XR 启动门控源码。
- 精简的 Unity uGUI 2.5.0 源码依赖及原许可说明；保留旧 TMP 换行字段兼容补丁。

**不包含** 原 VRCW 文件、从中提取的模型／纹理／原 Shader／声音／动画／世界场景／原程序数据。

## 构建

使用 **Unity 6000.5.10f1**，分别将 `PC/UnityProject`、`Android/UnityProject` 加入 Unity Hub。不要同时把两套 Assets 合并到一个项目。

首次打开需要恢复固定版本 UPM 依赖；除已嵌入的精简 uGUI 外，其余 Unity 包由官方包管理器获取。Android 的 PICO OpenXR SDK **不包含在源码包中**：manifest 指向官方 1.4.1 对应的固定提交 `3aa3e62bff41df618529eeb60ff02c29a515dafe`，需要 Git 和网络，以及接受／遵守 SDK 的适用条款。详见 [第三方说明](THIRD_PARTY_NOTICES.md)。不要导入 TMP 示例或 Essential Resources 包；项目已经提供不含字体的独立 TMP 配置。

Windows PowerShell 构建：

```powershell
# Windows Build Support
& .\PC\build.ps1 -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.5.10f1\Editor\Unity.exe'

# Android Build Support：SDK（含 API 36）、NDK r27c、OpenJDK
& .\Android\build.ps1 -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.5.10f1\Editor\Unity.exe'
```

默认生成 `PC/builds/WorldRelic/WorldRelic.exe` 和 `Android/builds/WorldRelic/WorldRelic.apk`。脚本拒绝覆盖已有产物或非空输出目录；重新构建请用 `-OutputPath` 指向新的空目录。Android 原生门控库从 `Android/native/WorldRelicXRPreInit.c` 编译，无需预先携带 `.so`。工具链不在 Unity 默认安装位置时，传 `-NdkPath`、`-SdkPath`、`-JdkPath`；未指定 SDK/JDK 则沿用 Unity 已配置的工具链。

这是源码交付，不附 Unity 编辑器、平台构建工具或可直接运行的 EXE/APK。脚本默认使用开发签名构建 Android；正式发行签名需自行配置。缓存、下载依赖和产物会在打开／构建后生成，均已加入 `.gitignore`。

## Windows 使用

编译后复制完整 Player 目录（EXE、Data、UnityPlayer 等），不能仅拷贝 EXE。将批准的 Windows 世界文件命名为 `AniqueMuseum-windows.vrcw`，与批准的字体文件一起放在 EXE 同目录，直接打开即可。文件名仅为兼容旧部署习惯，与应用品牌无关。

世界和字体均按完整 SHA256 识别，不需要 `-v`；字体可以改名，程序在 EXE 同目录按内容识别。也可以显式指定：

```powershell
& .\WorldRelic.exe --vrcw 'D:\UserContent\world.vrcw' --font 'D:\UserContent\VL-PGothic-Regular.ttf' --desktop
& .\WorldRelic.exe --vr
& .\WorldRelic.exe --help
```

未知／重复／冲突参数或未知输入拒绝运行，不以默认内容回退；显式错误路径不会回退到同目录文件。默认原生分辨率无边框全屏。支持 `--debug`、`--free-camera`、`--free-camera-noclip`；自由摄像机仅 PC 非 VR 生效，WASD 沿面向方向移动，前者保留碰撞／交互／传送且不受抛射／减速影响，后者同时关闭碰撞与传送。

## Android 使用

首次开启调用 Android 原生文件选择器，依次选择批准的 **Android** VRCW 和字体。保存持久只读 URI 授权，后续启动重新打开源文件并校验，文件移动／删除、权限失效或内容变化时重新选择对应文件，不以旧缓存绕过源访问检查。

普通手机自动走平面模式，已知 PICO 头显走 XR。

## 接受的外部文件

只接受以下精确字节；未来文件即便名字／版本标签相同也不会自动接受。

| 平台／类型 | 字节数 | SHA256 |
| --- | ---: | --- |
| Windows v134 security | 88,996,960 | `9DF63F11D07C971C21BE3239882DC7C5667B2D46F36C01325C8F1BEA123F7E2B` |
| Windows v135 raw | 91,207,817 | `314B3EEF87035E07A4B1EB02DD976F40A3648EB94AB4FAE961149E175615F66B` |
| Android v134 security | 79,968,961 | `0A8CBB2D37EBE948EABA0B5C2EC49B837276B661D051DAFA679ABFB9216A0ED7` |
| Android v135 raw | 81,674,354 | `ADC8209B247A5AF8722376932B3B496D86DCFF400F30E632500BC32A4274C930` |
| 两平台唯一外部字体 VL-PGothic-Regular.ttf | 4,149,288 | `9C32CC9CFF2D04AC95B0E8FCE51BA439083E473581B71CAE69AEAA8729E881B3` |

## 操作

- 键鼠：WASD／鼠标，Shift 跑，Space 跳，C/B 蹲下，E 交互／拾取，Q 放下，左键使用，F2 设置；特定双属性方块左键火／右键冰。
- 普通手柄：左杆移动，右杆视角，A 跳，B 蹲，X 交互，Y 拾取／放下，RT 使用，LB 跑；双属性方块 LT 火／RT 冰。
- VR：握持抓取，扳机操作，左 Y 切换手模拟／射线，右 B 蹲。射线只交互、不拾取；特定体验方块握持按一下拿起、再按放下，其他物件松握持放下。
- Android 平面触摸含蹲下；手柄接入隐藏触摸界面，主动触摸后显示，接入手柄时 5 秒无触摸再次隐藏，断开则恢复。普通手柄支持运行时插拔。
