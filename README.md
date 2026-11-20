# LanPin

当前源码：**v0.3.0 预发布版 — WPF 布局更新**。

面向 Minecraft Java 的 Windows 局域网发现与 IPv6 点对点联机工具。

## 下载

[v0.3.0 Release](https://github.com/hongmingniu/LanPin/releases/tag/v0.3.0) · [所有版本](https://github.com/hongmingniu/LanPin/releases)

完整解压对应版本的 Windows 程序包后运行 LanPin.exe，不要只复制 EXE。
v0.2.x / v0.3.0 为 WPF 版本，需要 .NET Windows Desktop Runtime（建议 .NET 10 x64）；v0.4.0 / v0.5.0 为 WinUI 3 x64，程序包随附 .NET 10 和 WinUI 运行文件。
所有版本均为实验软件，未经代码签名及独立安全审计。历史版本保留当时的行为和已知问题，请先阅读相应 Release 说明。

## 角色与联机

1. 想加入朋友世界的人选择 **加入方**，生成分享信息，并私下提供 IPv6、TCP 端口和 PIN。
2. 存档所在内网的一方选择 **存档接入方**，输入上述信息并连接；存档可位于其本机或所选内网另一台电脑。
3. 实际存档电脑在 Minecraft Java 中“对局域网开放”，并保持世界运行。
4. 加入方在多人游戏中寻找带 **〈LanPin〉** 前缀的存档；无法显示时使用软件提供的备用地址直接连接。

双方使用不同玩家身份，Minecraft 版本、模组和加载器保持兼容。
加入方监听端口默认 **38443**；Minecraft 存档端口例如 **5222** 是另一回事，不能混填。
手动游戏地址示例：`127.0.0.1:5222`，只用于存档接入方指定实际游戏地址。

## 网络条件与范围

双方 IPv6 须互通，加入方的公网 IPv6 与 TCP 监听端口须可被存档接入方访问。
只有 `fe80::` 本地链路地址、没有 IPv6 默认路由的网络不能按现有方案进行外网直连。
交换现有角色不能解决一方完全没有 IPv6 的问题。程序不提供 IPv4 回退、中继、STUN、二层 VPN、通用虚拟 IP 或自动重连，也不修改系统防火墙、路由和代理。

Java 局域网公告使用 UDP 4445 / 224.0.2.60，两端应选择正确的游戏网卡。
128 位随机 PIN 配合绑定 TLS 服务端证书的双向 HMAC 验证；PIN 仅用于当前配对，请私下传递。

## 构建与检查

完整源码位于 [src/LanPin](src/LanPin)。该版本的依赖、构建要求、许可证及检查入口详见 [源码说明](src/LanPin/README.txt)。

```powershell
dotnet publish src/LanPin/LanPin.csproj -c Release -o build/LanPin
.\build\LanPin\LanPin.exe --self-test
```

WPF 版本通常可使用 .NET SDK 8 或更新版本；WinUI 版本使用 .NET SDK 10 与 Windows 开发环境。使用原生玻璃桥接的版本还需要 Visual Studio C++ 工具与 Windows SDK。
对应程序包包含原始验证范围说明，本次历史发布的复核结果另见 Release 附件 `Publication-Checks.txt`。
自动检查不等同于真实鼠标拖动延迟或桌面玻璃的实机视觉验收。
Minecraft Java 1.21.11 的真实客户端验证来自此前同机双实例测试；这些界面版本尚未重新完成异地双机或双虚拟机游戏验收。

## 历史版本

| 版本 | 内容 |
| --- | --- |
| [v0.2.0](https://github.com/hongmingniu/LanPin/releases/tag/v0.2.0) | WPF 外观预览 |
| [v0.2.1](https://github.com/hongmingniu/LanPin/releases/tag/v0.2.1) | WPF 桌面玻璃初版 |
| [v0.2.2](https://github.com/hongmingniu/LanPin/releases/tag/v0.2.2) | WPF 玻璃兼容修正 |
| [v0.2.4](https://github.com/hongmingniu/LanPin/releases/tag/v0.2.4) | 独立合成实验 |
| [v0.2.5](https://github.com/hongmingniu/LanPin/releases/tag/v0.2.5) | WPF 渲染与圆角修正 |
| [v0.3.0](https://github.com/hongmingniu/LanPin/releases/tag/v0.3.0) | WPF 布局更新 |

v0.2.4 存在内容遮挡的严重显示回归，仅供历史研究；v0.4.0 的主题、动效和玻璃观感尚不完整。
本仓库 `downloads` 和 `docs` 中保留的 v0.1.1 程序及 PDF 为旧版资料，网络角色与条件仍可参考，界面以当前版本为准。

## 许可证

项目源码遵循 [GNU GPL v3](LICENSE)。字体、Microsoft 运行文件及其他第三方组件适用各自许可证和 NOTICE，详见每个版本随附的 Licenses 目录。
HarmonyOS Sans 字体保持原始文件，Copyright 2021 Huawei Device Co., Ltd.
