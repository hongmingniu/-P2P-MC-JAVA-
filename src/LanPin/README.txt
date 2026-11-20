LanPin v0.5.0 — WinUI 3 预览版

运行
Windows 10 22H2 / Windows 11 x64。整个文件夹解压后运行 LanPin.exe，不要只复制 EXE。
本包随附 .NET 10 与 Windows App SDK / WinUI 运行文件，无需另外安装 .NET 或 Windows App Runtime。
请关闭旧版再运行新版；首次启动可能比后续启动慢。程序尚未代码签名。

本版界面
实际使用 Microsoft.UI.Xaml / WinUI 3，不加载 WPF，也不使用浏览器界面。
采用 ChatAIBox 已验证的 WinUI 窗口与主题实现，适配为 LanPin 的侧栏、首页和连接抽屉。
支持跟随系统、浅色与深色，保留自定义背景和三种默认色调。
页面使用 250 毫秒缓动入场，按钮具有悬停和按下反馈，可启用减少动效。
按钮、输入框、角色选择、展开区域、滑块、文件选择器均为原生控件。
自绘标题栏按钮，原生窗口命中测试处理拖动、缩放、最大化；客户区去除系统标题栏留边。
Windows 10 使用独立合成 HostBackdrop 玻璃层，Windows 11 使用 WinUI DesktopAcrylicBackdrop；系统关闭透明效果、高对比度、节能策略或设备环境可能令其回退为不透明背景。
Windows 10 的圆角使用原生窗口区域兼容处理，Windows 11 优先使用系统圆角。
内置未修改的 HarmonyOS Sans SC 字体。窗口按钮由 WinUI 控件绘制。

联机
1. 点击“开始联机”。想加入朋友世界的人选择“加入方”，确认对方能访问的 IPv6，点击生成分享信息。
2. 私下发送 IPv6、TCP 端口和 PIN。默认端口 38443，需允许朋友访问。
3. 在实际存档所在内网，朋友选择“存档接入方”，输入上述信息并连接。
4. 实际开服电脑在 Minecraft Java 中对局域网开放并保持世界运行；它可以是接入方本机，也可以是内网另一台电脑。
5. 加入方在多人游戏中寻找带〈LanPin〉前缀的存档。列表不出现时，可从“查看存档”复制备用地址直接连接。
网卡、端口和手动游戏地址在“网络选项”中。手动地址示例 127.0.0.1:5222。
运行中可通过“连接详情”重新查看当前参数。关闭抽屉不会断开连接，“断开”会释放当前会话并清空 PIN 和存档列表。
双方 IPv6 需互通，Minecraft 版本、模组和玩家身份需兼容。不可使用同一玩家身份同时加入。
没有中继、自动重连或通用虚拟 IP。程序不改变系统路由、代理或防火墙。

背景
外观页可导入 PNG / JPG / BMP，或将一张图片拖进窗口，最大 24 MB。
图片会复制到本机 LanPin 数据目录，不修改原始文件、不上传。最长边解码限制为 1600 像素。
背景图模糊在参数变化后于后台计算，缓存结果用于显示，不在窗口拖动时重新模糊整张图片。
支持磨砂遮罩、背景图混合、抽屉磨砂浓度、完整显示与减少动效。
“恢复默认背景”只取消选用，不删除原始文件和历史缓存。
外观设置与此前版本兼容，保存在 %LOCALAPPDATA%\LanPin，不保存 PIN、IP 或账号。

检查与限制
本版完成原生 WinUI 窗口启动、背景处理、设置保存和双窗口联机集成检查，详细范围见“验证范围.txt”。
自动检查使用真实 IPv6 回环 TLS、局域网发现格式的组播公告和 TCP 数据流。
排版预览由程序内部渲染，无法完整记录窗口背后的桌面玻璃。
没有在本版重新执行真实 Minecraft 或异地双机游戏验收，未测量实际鼠标拖动的输入延迟或帧率。
联机日志仅保留在当前进程内存；严重启动或未处理的界面错误会写入本机 LanPin 数据目录用于排错。分享排错内容前，请检查其中的内网地址、文件路径等个人信息。

开发
完整应用源码在 src/LanPin。需要 .NET SDK 10、Visual Studio 2022 C++ 桌面开发工具与 Windows SDK。
Native/build.cmd 构建随包的 HostBackdrop 原生桥接 DLL；无需在用户机器上编译。
项目固定 Windows App SDK WinUI 2.3.9 组件版本，依赖记录于 packages.lock.json。
dotnet publish src/LanPin/LanPin.csproj -c Release -o build/LanPin
LanPin.exe --self-test：核心网络自测，结果写入程序目录。
LanPin.exe --ui-check <输出目录>：独立配置的 WinUI 原生界面及联机检查，并输出排版图。

许可证与第三方
应用源码依照附带的 GNU GPL v3.0 许可证提供。
Microsoft .NET、Windows App SDK / WinUI、Windows SDK 投影及关联运行库分别适用其附带的许可和 NOTICE，不适用本项目的 GPL 授权。
本应用使用未修改的 HarmonyOS Sans 字体，Copyright 2021 Huawei Device Co., Ltd.
字体完整许可见 Licenses/HarmonyOS-Sans-LICENSE.txt；字体不可作为独立字体软件分发。
相关许可、第三方声明与来源信息均在 Licenses 目录。
