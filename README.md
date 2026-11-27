# Vedal AI · Twin Shift

**当前公开安装包为 v1.1.0-beta.1。** Beta 4 将八句语音随包安装，首次播放即可离线使用。[公开版本和 SHA-256 清单](https://github.com/DZHI0019/vedal-ai-twin-shift/releases)

给 Windows 版 Codex 的 Neuro-sama、Evil Neuro 和 Vedal 同人主题。Neo 的「月之亮面」使用暖纸色和 IBM 5100 电台，Evo 的「月之暗面」使用近黑灰阶和地下室场景。代码仍有清晰的增删标记。Vedal AI 是主题署名，模型名称和权限审批由 Codex 显示。

## 使用

1. 运行所选版本的 `TwinShift-<版本>-Setup.exe`。安装面向当前 Windows 用户，需要 .NET Framework 4.8。打开开始菜单中的 **Twin Shift**。
2. 在控制台「外观」备份 Codex 原有的两槽主题，分别将「月之亮面」和「月之暗面」导入 Codex「设置 → 外观」中的浅色和深色槽。选择系统模式后，窗口配色会随系统外观切换。侧栏小龟可以单独开启。
3. 想看两幅场景时，将「打开两套壁纸包」中的 ZIP 导入 Dream Skin，再从 Saved Themes 选择。它是可选的第三方宿主，每套主题使用一张图。安装、恢复和重启由使用者决定。[Dream Skin v1.5.21](https://github.com/Fei-Away/Codex-Dream-Skin/releases/tag/v1.5.21)
4. 打开「语音」，点台词旁的 **播放**。公开版内置八句同人合成配音。通过 **⋯ → 使用本地 WAV…** 可替换任意一句，选择 **恢复内置语音** 可撤销。
5. 在「接入」启用事件插件，在 Codex 的 Hooks 设置审阅该插件并打开新聊天。在「语音」选择是否自动播放测试结果。控制台可收进托盘。审批、提问、测试成功、失败、中断和普通回合结束分别处理。

| 场景 | 可以看到、听到的反馈 |
| --- | --- |
| 阅读与写代码 | 白班使用深苔绿主操作、暖纸色表面和绿灰色文字。壁纸包可修饰侧栏、输入框和对话框。窗边 Vedal 可以点，窄窗口自动收起。 |
| 查看用量 | 在 Codex 支持的聊天中输入 `/status`。原生字符刻度沿用主题前景，百分比、周期、重置时间和窄窗布局由 Codex 提供。控制台无需读取账户额度。 |
| 等待审批 | 原生审批按钮和命令继续显示；可单独开启短提示。 |
| 有通过报告 | 白班 Heart 和「Yay!」，夜班管道和「I win? Let’s go!」。通过记录提供 Vedal 和管道彩蛋。 |
| 有失败报告 | 显示失败；夜班可播放「Aww, shucks.」。普通回合结束只显示文字。 |

语音从本地读取。自动语音和窗边装饰默认关闭。主题程序没有后台更新服务或遥测。安装本身不会启用插件、应用配色或重启 Codex。恢复、片源和手动卸载步骤见[使用手册](docs/使用手册.html)。

### Beta 兼容情况

在 Windows Codex 26.1120.6357.0 上，beta.2 可通过「接入 → 适配新版标题栏」为 Dream Skin 1.5.21 启用菜单与标题栏着色。从 beta.1 升级时请重新导入安装包里的两份壁纸 ZIP，再从 Saved Themes 选择。「还原宿主标题栏」可恢复上游原文件。Neo 和 Evo 的独立测试首页均通过宿主的必需部件检查，输入框完整可见。

小龟可绑定一个专用 Codex 实例。真实审批、代码 diff、任务完成和窗边小龟的同屏验收仍在进行。公开 beta.1 使用未签名安装程序。候选版的签名构建与验签方法见维护手册。

## 构建

在 Windows 上使用 PowerShell 7、.NET SDK 8 或更高版本、Python 3.9+ 和 Node.js 22+。生成 EXE 安装包另需 Unicode NSIS 3。NuGet 锁文件纳入版本控制；运行程序使用系统 .NET Framework 4.8。

```powershell
./tools/build.ps1
./tools/test.ps1
./tools/build.ps1 -MakeNSIS 'C:\Program Files (x86)\NSIS\makensis.exe'
./tests/installer.ps1 -Installer './out/TwinShift-1.1.0-beta.4-Setup.exe'
```

编译产物在 `out/`。主题源、素材署名、版本兼容和复验步骤见[维护手册](docs/MAINTAINING.md)。欢迎提交可复现的问题，附 Codex、Windows、主题版本和操作步骤即可。发截图前请遮盖项目名、聊天内容和个人路径。

## 许可

代码及原创界面图标采用 [MIT](LICENSE)。像素乌龟、生成的同人场景与内置配音的署名和许可分别列在[素材署名](assets/NOTICE.md)。本项目为非官方同人作品。
