# 维护Vedal AI

## 代码与接口

| 修改内容 | 维护位置 | 需要复验 |
| --- | --- | --- |
| 原生主题和 diff 配色 | `themes/*.json` 是源，`*.txt` 是 Codex 可粘贴的分享格式 | 重建单行分享文本，运行 `node --test themes/theme.test.mjs`。需同时检查两槽、错误槽和对比度。 |
| 日常窗口场景 | `app/WindowScene.cs`、`app/BadgeWindow.cs` | 检查子窗口归属、DPI、鼠标穿透和两套插画。 |
| Dream Skin 输入框、侧栏、对话框 | `scenes/`、`assets/neo.jpg`、`assets/evo.jpg`、`tools/build_scenes.py` | 用固定版本 Dream Skin 验证器和 Windows 导入器。相同输入两次构建应得到相同 ZIP 哈希。 |
| 装饰与偏好 | `app/MainWindow.xaml`、`MainWindow.xaml.cs`、`BadgeWindow.cs`、`Glyph.cs`、`LocalState.cs` | 亮、暗、高对比度、减弱动效、窄窗、前台切换和恢复偏好。新增偏好应有适用于旧文件的默认值。 |
| 宿主事件 | `plugin/hooks/hooks.json`、`signal/Program.cs`、`shared/SignalBus.cs`、`app/Receiver.cs` | 首先检查 Codex 真正返回的载荷。覆盖正常、失败、超时、重复、空套件与无法分类的输出。 |
| 语音及来源 | `assets/voices.json`、`app/AudioDeck.cs`、`app/VoiceOutput.cs` | 核对角色、语义、原片、剪辑整理者、授权范围、内容哈希、时长和相对响度。核对离线首播、文件缺失、校验失败和自选 WAV 优先级。 |
| 安装和升级 | `tools/build.ps1`、`packaging/TwinShift.nsi`、`app/PluginManager.cs` | 包文件清单、无管理员安装、覆盖升级、外来目录拒绝、卸载与保留用户数据。 |

## 兼容基线

### 按钮和用量显示

neuro 的主操作取龟壳的苔绿 `#5D7559`，正文和随文字着色的刻度用 `#2B3932`，浅底为 `#F9F4EC`。控制台每组最多突出一个操作；键盘焦点、按下和禁用沿用按钮状态。色值测试覆盖主操作图标、正文、次要文字与 diff。设计参考 [GitHub Primer 的按钮层级](https://primer.style/product/components/button/)、[进度与可见数值](https://primer.style/product/components/progress-bar/) 和 [WCAG 非文本对比度](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html)。

在上述桌面版本中，`/status` 的用量条由实心与浅色块字符组成，实际宽度决定段数。用量菜单另有百分比和重置时间。Dream Skin 1.5.21 的安全 CSS 部件包含输入框和工具栏，没有独立用量条或发送按钮选择器。主窗口通过原生前景和场景强调色着色，避免全局按钮规则影响警告或审批。检查新客户端时，分别看宽窗、窄窗和系统高对比度。

本版对照 Windows Codex 桌面版 26.1120.6357.0，配套 CLI 为 0.171.0-alpha.13.2。配色走设置页导入；项目在该版本实际解析器上检查了标准化结果、非法色值和槽位错误。日常窗口中的插画由 WindowScene 挂在 Codex 的 Windows 子窗口上，按客户区大小布局。另提供适配 Dream Skin 1.5.21 的场景包。控制台和事件接收仅在 Windows 上提供。将来宿主修改内部布局时，应先验证原生配色，再判断是否需要升级可选壁纸包。[Codex 外观设置](https://learn.chatgpt.com/docs/reference/settings#appearance) · [Dream Skin 验证器](https://github.com/Fei-Away/Codex-Dream-Skin/blob/v1.5.21/runtime/theme-package-validator.mjs)

Closure-Codex 的装饰样式、色板和独立预览测试是这套设计的参照。这里沿用按事件区分语音的做法，复用 Dream Skin 的限定 CSS 和 ZIP 校验。Get Codex Theme 的主页、任务页和窄窗案例也可用于复查。自己的发布包仅含本项目的程序和已列入署名表的素材。[Closure-Codex 装饰实现](https://github.com/OndoP3901/Closure-Codex/tree/main/outputs/closure-decoration) · [Get Codex Theme](https://github.com/ViisOpen/get-codex-theme) · [Dream Skin 安全说明](https://github.com/Fei-Away/Codex-Dream-Skin/blob/v1.5.21/SECURITY.md)

## 事件、声音和隐私

Codex 插件仅声明五类异步命令钩子。Windows 默认命令解释器展开 `%PLUGIN_ROOT%`，程序从标准输入接收受限长度的 JSON，处理后静默退出。控制台关闭时直接返回。命名管道限制为当前 Windows 用户并拒绝网络身份，只发送枚举事件、去重摘要与聊天短标识。任务列表在内存里保留最近 12 项，退出即清除。同一账户下的其他程序具有相同的本地身份，装饰事件不可用作权限或安全决策。[Codex Hooks](https://learn.chatgpt.com/docs/hooks) · [插件结构](https://developers.openai.com/plugins/build/plugins)

普通 Stop 表示回合结束。只有受支持的独立 Playwright 或 node:test 命令及其可识别的完整报告，才构成测试结果反馈。已知退出码与报告冲突时保持安静。后台尚在运行、命令链、测试发现与零测试均不得报喜。审批仅提示，无论是否开启语音，都需要用户在 Codex 决定。一次普通结束紧随测试时，刚完成的结果会短暂留在面板中。自定义 Hooks 解释器、第三方测试脚本和其他报告器需另外适配。

八段直播原声在 `assets/voices/`。源码和安装包使用同一组 MP3，构建、安装测试和播放器均核对 `assets/voices.json` 的 SHA-256。更新某段剪辑时，先核对带出处的原件及其再分发条款，再一同更新该段的署名、时长、音量和摘要。自选 WAV 保存在个人数据目录。

## 复验与发行

基础测试在全新本地配置中解码八个随包文件。开发机有可用的 Windows 音频输出时，加 `-CheckAudioOutput` 逐一触发原生播放按钮并检查各自的输出电平。`-CheckCodex` 使用隔离的 CLI 配置。

```powershell
./tools/build.ps1
./tools/test.ps1 -CheckAudioOutput -CheckCodex -WallpaperEngine ../Codex-Dream-Skin/windows
node ./tests/host-hooks.mjs ./out/app
./tools/build.ps1 -DreamValidator ../Codex-Dream-Skin/runtime/theme-package-validator.mjs -MakeNSIS 'C:\Program Files (x86)\NSIS\makensis.exe'
./tests/dream-skin.ps1 -UpstreamRoot ../Codex-Dream-Skin
./tests/installer.ps1 -Installer ./out/TwinShift-1.1.0-beta.6-Setup.exe
```

Dream Skin 源码应固定到 v1.5.21，Windows 导入器需要上游认可的签名 Node.js。CI 包含编译、原生窗口、离线音频、事件通道和配色验证。发布前在隔离桌面配置导入两槽和两图，核对审批控件可点击、变更可读、可还原；使用物理音箱时再复查相对音量。在用户桌面另行核对视觉效果。

### 标题栏与隔离实例

宿主兼容适配固定在 Dream Skin v1.5.21（提交 `6fc42df48a6a5365011f55cc349e20c038e56d1d`）。上游 `tools/selectors.json` 中的 `header-tint` 对应两个标题表面：主区标题栏与 Windows 应用菜单。维护本适配时，在上游源码修改该选择器，再运行 `node tools/sync-runtime-assets.mjs`。核对 Windows 生成的 CSS、渲染器和选择器清单的字节及 SHA-256。控制台会在写入前检查三份文件；已知的中间状态支持重新应用或还原。用户更新到其他宿主版本后，需重新核验选择器。

在独立桌面配置中，可用 `TwinShift.exe --tray --window-pid <测试窗口进程号>` 启动伴侣。启动前将 `TWINSHIFT_DATA_DIR` 指向专用目录，并在该配置开启小龟。绑定仅在本次进程有效。验收时把脚本生成的检查结果与用户可见的画面分别记录，截图存放在公开源树之外。

### Windows 发布签名

在项目获授权的 Windows 签名机上使用 SDK 的 `signtool.exe`。下列参数适用于当前用户证书库中可用的硬件或托管密钥。维护者需提前确认所选发布者的公开身份适合展示在安装包属性中。私钥保留在签名端。普通编译写入 `out/`，带签名构建通过验证后写入 `out/signed/`。

```powershell
./tools/build.ps1 -Signed -MakeNSIS $makeNsis -SignTool $signTool -SignerThumbprint $publicThumbprint
./tests/installer.ps1 -Installer $installer -SignTool $signTool -SignerThumbprint $publicThumbprint
```

构建先校验发布者证书，再依次签署伴侣、事件程序、NSIS 卸载器和最终安装包。文件摘要与 RFC 3161 时间戳均采用 SHA-256。任一步失败都会中止发布。发布前在采用默认信任库的干净 Windows 环境再次运行第二条命令，核验四个可执行文件和安装、卸载流程。对由外部机构签署的成品也可单独运行此验收。Windows SDK 和 NSIS 的相关接口见[微软 SignTool 文档](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)及 [NSIS 编译器文档](https://nsis.sourceforge.io/Docs/Chapter5.html#uninstfinalize)。

版本变更时同步程序集、插件描述、场景包版本和使用手册。NSIS 从 `Directory.Build.props` 读取版本。通过检查后从干净检出构建安装包，从相同提交导出源码，记录提交、工具版本、SHA-256 与发布者签名状态。
