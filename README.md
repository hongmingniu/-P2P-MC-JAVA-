# LanPin

当前版本：**v0.1.1 实验版**。角色统一为 **加入方** 与 **存档接入方**，连接流程不变。

面向 **Minecraft Java** 的 Windows 局域网发现与 IPv6 点对点联机工具。

加入方生成 PIN，存档接入方输入加入方的 IPv6、端口和 PIN 连接；软件转发游戏的局域网公告和 TCP 连接，让远端存档出现在加入方的多人游戏列表中。

## 下载

[最新 v0.1.1 Release](https://github.com/hongmingniu/-P2P-MC-JAVA-/releases/tag/v0.1.1)

- [Windows 程序包](downloads/LanPin-Windows.zip)：程序、两份 PDF、许可证和对应源码。
- [加入方使用说明](docs/LanPin-加入方使用说明.pdf)：想加入朋友存档的人。
- [存档接入方使用说明](docs/LanPin-存档接入方使用说明.pdf)：接入本机或内网存档的一方。

完整解压程序包后运行 `LanPin.exe`，不要只复制 EXE。Windows 10/11 x64；需要 [.NET Windows Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。程序按 .NET 8 API 编译，允许使用更新主版本桌面运行时；建议安装仍受支持的 .NET 10。

## 如何使用

1. **加入游戏的人选加入方**，选择 Minecraft 所在网卡，点击“开始分享 / 连接”。
2. 加入方将 IPv6、TCP 端口（默认 `38443`）和 PIN 私下提供给存档接入方。
3. **存档所在内网的一方选存档接入方**，选择存档所在内网的网卡，填写上述信息并连接。
4. 在实际存档电脑上进入 Minecraft Java 存档，选择“对局域网开放”，并保持游戏运行；存档电脑可与存档接入方是同一台，也可位于其内网。
5. 加入方进入多人游戏列表，选择带 **〈LanPin〉** 前缀的局域网存档加入。
6. 列表未显示时，双击软件中的存档条目复制转发地址，使用 Minecraft“直接连接”。

双方需要不同的玩家身份，以及兼容的游戏版本、模组和模组加载器。存档电脑返回主菜单会停止存档服务器；重新开放后游戏端口或转发端口可能变化。

## 网络条件

- 双方 IPv6 必须互通，加入方 IPv6 地址及监听端口须能被存档接入方连接。自动显示的地址只是候选值，可手动更正。
- 加入方按需放行指定的 TCP 入站端口；程序不修改系统路由、代理或防火墙。
- Java 局域网发现使用 UDP `4445`、IPv4 组播 `224.0.2.60`，两端各自选择正确的游戏网卡。
- 存档接入方无法自动发现时，可在连接前填写游戏地址，例如 `127.0.0.1:5222`，或所选内网中的 `192.168.1.10:5222`。
- `38443` 是 LanPin 端口，`5222` 等是游戏端口，两者不要混填。
- 不提供中继、STUN 或无可用 IPv6 时的回退；PIN 无法绕过网络入站阻断。

## 范围和限制

这是应用层游戏隧道，不是二层 VPN，不创建通用 `192.168.x.x` 虚拟网卡，也不支持基岩版。

每次分享仅配对一个存档接入方，最多保留 16 个存档、32 条游戏 TCP 连接；只允许加入方本机连接本地代理。存档接入方只转发选定网段内发现或明确指定的游戏端口。停止或网络中断后需要重新配对，此版不自动重连。

传输使用 TLS 1.2/1.3；128 位随机 PIN 配合绑定 TLS 服务端证书的双向 HMAC-SHA256 验证。等待配对最长 10 分钟，停止分享后 PIN 失效。PIN 应私下传递，排错日志不应包含 PIN。

软件不保存配对 PIN，不提供第三方中继服务；实际网络出口仍受系统和网络配置影响。这是未经独立安全审计及代码签名的实验软件。

## 构建

需要 Windows 和 .NET SDK 8 或更新版本，无第三方 NuGet 依赖：

```powershell
dotnet publish src/LanPin/LanPin.csproj -c Release -o build/LanPin
```

## 验证

```powershell
.\build\LanPin\LanPin.exe --self-test
```

自测完成后在程序目录生成 `self-test-result.txt`；退出码 0 表示通过。覆盖 IPv6 TLS、正确/错误 PIN、证书绑定、Java 组播发现、并发数据转发和端口释放。

已在 Minecraft Java **1.21.11 同机双实例**中验证真实客户端自动发现、经转发登录和进入世界。尚未完成双虚拟机、异地公网或跨路由器验收；本机自测不能替代这些场景。

## 宣传片

- [观看 / 下载宣传片](https://github.com/hongmingniu/-P2P-MC-JAVA-/releases/download/v0.1.1/LanPin-Promo-v0.1.1.mp4)
- [HTML 动画源码](https://github.com/hongmingniu/-P2P-MC-JAVA-/releases/download/v0.1.1/LanPin-Promo-HTML-v0.1.1.zip)

## 许可证

[GNU GPL v3.0](LICENSE)。程序包随附许可证及对应源码；.NET 运行时需另行安装，其许可由相应权利人提供。
