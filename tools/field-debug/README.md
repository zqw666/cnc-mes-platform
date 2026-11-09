# 现场离线调试包

## ⭐ 首选路径已变更（2026-09-11 更新）

**前提变化：开发机与工控机之间可以拉网线。** 这一条彻底改变了打法 ——
物理链路一通，Tailscale / frp / 任何穿透方案都不需要，现场"禁 U 盘 + 只能传文字"的限制也一并绕过。

👉 **请直接看 [`direct-cable/`](./direct-cable/README.md)**，那里是当前的主力方案：

| 脚本 | 运行位置 | 作用 |
|---|---|---|
| `connect-ipc.ps1` | 开发机（管理员） | 一键配双网段 IP + Ping/端口全探测 + 出文字报告 |
| `serve-files.ps1` | 开发机 | 极简 HTTP 文件服务，把部署包/日志推给工控机 |
| `ipc-setup.ps1` | 工控机（管理员） | 端口转发 8888 + 防火墙（后端只监听 localhost 的解法），支持 `-Status` / `-Undo` |
| `collect-evidence.ps1` | 工控机或开发机 | **取证**：数据库表/步骤/日志 + 部署核对 + 事件日志，一次抓齐"程序停在哪" |

本目录根下的脚本（`check-net` / `set-dev-net-ip` / `test-ports` / `ipc-enable-remote`）
已被 `direct-cable/` 里的新脚本覆盖，**保留仅作参考**；撤销 IP 现在用 `connect-ipc.ps1 -Restore`（`restore-net.ps1` 亦可，但只认 125.210 一个地址）。
`text-transfer/`（base64 文字通道）在**网线还没到位、只能先靠内部平台传文字**时仍然是唯一的进场手段。

---

> 以下为「无法拉网线、必须完全离线」时的原始方案。
> 适用前提：**现场整片区域无外网**，无法使用 Tailscale / frp / 向日葵 / ToDesk 等任何穿透方案。
> 且 **U 盘禁接、只能通过内部平台传文字**。
> 工控机：`192.168.125.201`　|　设备网：`192.168.101.x`　|　后端 HTTP 端口：`8888`
>
> ⚠️ 在「只能传文字」的约束下，本目录的 `.ps1` 脚本需要靠 base64 文本送进现场。
> 请看 **`text-transfer/`** 目录的《文字通道作战手册》——那里的**路径 A（把闭环搬到工控机）**
> 才是这个场景的主力方案，本目录的脚本属于「有物理链路时」的用法。

---

## 一、先判断：还能不能远程

无外网 **不等于** 不能组网。真正决定成败的是「办公区到车间有没有物理链路」：

| 现场条件 | 能否远程 | 做法 |
|---|---|---|
| 办公区与车间在同一张厂区网内（过路由器，只是网段不同） | ✅ 可以 | 加静态路由 + 工控机开端口转发，见 §四 |
| 两点之间可拉网线/光纤，或可架无线网桥 | ✅ 可以 | 先做物理链路延伸，再按 §四配置，见 §五 |
| 完全物理隔离且无外网 | ❌ 远程无解 | 只能带笔记本到现场，见 §三 |

结论：**唯一能一次性解决远程问题的，是给办公区到车间补一条物理链路。** 其余方案都需要人到现场。

---

## 二、脚本清单

| 脚本 | 在哪台机器执行 | 作用 | 是否改配置 |
|---|---|---|---|
| `check-net.ps1` | 任意 Windows | 只读诊断：网卡、路由、到设备网的连通性 | 否 |
| `set-dev-net-ip.ps1` | 笔记本（现场） | 给指定网卡配 `192.168.125.210/24`，不设网关 | 是 |
| `restore-net.ps1` | 笔记本（现场） | 删除上面的调试 IP，恢复原状 | 是 |
| `ipc-enable-remote.ps1` | **工控机**（管理员） | 开端口转发 `0.0.0.0:8888 → 127.0.0.1:8888` + 放行防火墙 | 是 |
| `test-ports.ps1` | 任意 Windows | 测试目标 IP 的 8888 / 3389 / 4026 / 445 / 1433 端口 | 否 |

### 怎么运行这些脚本

脚本是 PowerShell 脚本，**双击默认是打开记事本**，请按下面任一方式运行：

- 右键脚本 → **使用 PowerShell 运行**
- 或在 PowerShell / cmd 里执行：

```
powershell -ExecutionPolicy Bypass -File .\check-net.ps1
```

需要管理员权限的两个脚本（`set-dev-net-ip.ps1`、`ipc-enable-remote.ps1`）
会自己弹出 UAC 提权窗口，**无需手动用管理员开 PowerShell**。

如果提示"禁止运行脚本"，就是执行策略挡住了，按上面的命令加 `-ExecutionPolicy Bypass` 即可。

---

## 三、现场操作流程（推荐路径）

### 出发前在办公机准备

- 把整个 `field-debug` 目录拷进 U 盘
- 把后端编译产物拷进 U 盘，**连同 `.pdb` 文件**（要断点调试就必须有 PDB）
- 如需断点调试，把 VS 远程调试器 `msvsmon.exe`（x64，`Remote Debugger` 目录）一并拷进 U 盘

### 到现场后

1. 用网线把笔记本的 **USB 网口**接到现场交换机。
   保留 WLAN 不动——默认路由仍走办公网，不影响笔记本原有上网。
2. 运行 `set-dev-net-ip.ps1`（会自己申请管理员权限），按提示选择 USB 网口的 `ifIndex`。
3. 运行 `check-net.ps1`，确认 `192.168.125.201` 可达。
4. 浏览器打开 `http://192.168.125.201:8888/`。
   - 打不开就先在工控机跑一次 `ipc-enable-remote.ps1`（见 §四）。
5. 需要断点调试时，在工控机启动 `msvsmon.exe /noauth /anyuser /port 4026`，
   VS 里「调试 → 附加到进程 → 传输：远程(无身份验证) → 目标 `192.168.125.201:4026`」。
   注意子程序是 `Process.Start` 拉起的短命进程，要在它跑起来后手动附加。
6. 调试结束，运行 `restore-net.ps1` 清掉调试 IP。

### 关于 101 设备网

笔记本接在 125 段时，**ping 不通 `192.168.101.x` 是正常的**——两个网段之间必须由工控机做转发或 NAT。
如果这次主要调 PLC / 机床：

- **首选**：把网线直接插到设备网（101 段）的交换机，用 `set-dev-net-ip.ps1` 改配 `192.168.101.210/24`；
- **或者**：在工控机上开 NAT 把两段打通（见 §四进阶），这样一根线通吃。

---

## 四、让工控机可远程访问（改一次，长期有效）

在工控机上**以管理员身份**运行 `ipc-enable-remote.ps1`，它会做三件事：

```
netsh interface portproxy add v4tov4 listenaddress=0.0.0.0 listenport=8888 connectaddress=127.0.0.1 connectport=8888
netsh advfirewall firewall add rule name="MachineMonitor-8888" dir=in action=allow protocol=TCP localport=8888
```

**为什么需要这一步**：`Program.cs` 只注册了 `http://localhost:8888/` 和 `http://127.0.0.1:8888/` 两个前缀，
程序本身不接受外部连接。用 portproxy 在外面套一层转发，**程序一行都不用改**。

验证方式：在工控机上执行 `netsh interface portproxy show v4tov4` 应能看到 8888 那行。

> 备选方案（不想用 portproxy 时）：
> 在 `Program.cs` 里加 `listener.Prefixes.Add($"http://+:{AppState.Port}/");`，
> 但必须**以管理员身份运行**程序，并先执行 `netsh http add urlacl url=http://+:8888/ user=Everyone`，
> 否则会抛 AccessDenied。绑 `0.0.0.0` 后设备网上任何机器都能打开控制页，建议同时加个简单 token 校验。

### 进阶：让 125 与 101 两段互通

在工控机上执行（PowerShell，管理员）：

```powershell
New-NetNat -Name FieldNat -InternalIPInterfaceAddressPrefix 192.168.101.0/24
```

它把 101 段当作内网、125 段当作外网，来自笔记本的访问会被源地址转换，
PLC / 机床的回包直接回到工控机再由其转回笔记本，**不需要改 PLC 的任何路由配置**。
删除用 `Remove-NetNat -Name FieldNat`。

---

## 五、不想每次都跑现场：补物理链路

按成本从低到高：

1. **拉网线** —— 两点直线距离 100 m 以内，走桥架/线槽，两端各接一台交换机。
2. **拉光纤** —— 距离更远或有强电干扰时，用单模光纤 + 一对光收发器，几百米到几公里都行。
3. **无线网桥**（最推荐）—— 一对 5 GHz 室外网桥（TP-Link CPE 系列、Ubiquiti 等），
   要求两点**视距可见**，100~500 m 很稳、1 km 以上需上高增益天线。
   链路两端各挂一台交换机即可，全程不需要互联网。
4. **4G 路由**（如果现场只是没固网、但允许通信）—— 插一张流量卡即可让 Tailscale 复活，
   带宽需求极低（几十 KB/s 足够），这是最省事的方案，前提是政策允许。

链路通了之后，再按 §四 配置工控机，办公机就能直接开 `http://192.168.125.201:8888/`。

---

## 六、设备地址清单（取自现场 config.json）

| 用途 | 地址 | 端口 / 协议 |
|---|---|---|
| 工控机（后端 HTTP） | `192.168.125.201` | TCP 8888 |
| PLC | `192.168.101.10` | 9600 |
| 机床 1 | `192.168.101.53` | OPC UA 4840 |
| 机床 2 | `192.168.101.52` | OPC UA 4840 |
| 机床 3 | `192.168.101.51` | OPC UA 4840 |
| 扫码枪 | `192.168.101.71` | TCP 5000 |
| 外部 MES（Eman） | `172.16.129.243` | SQL Server 1433 |
| NC 共享目录 | `\\192.168.1.100\share` | SMB 445 |
| 本地数据库 | `127.0.0.1` | SQL Server 1433（库 `qianduantest`） |

> 注意：现场是**双网段结构**，PLC / 机床 / 扫码枪全在 `192.168.101.x`，
> 只有工控机自己在 `192.168.125.x`。Eman 与 NC 共享又是另外两个网段。

---

## 七、完全无法组网时：改成异步离线调试

网络不通不代表不能排查，把"在线调试"换成"跑一轮、拷日志、回来分析"：

### 7.1 日志在哪里（重要）

`LogHelper` 默认配置是 `FileEnabled = false`、`DatabaseEnabled = true`
→ **日志写进数据库 `process_log` 表，不落文件**，别去目录里找 `.log`。

在工控机上查最近日志：

```cmd
sqlcmd -S 127.0.0.1,1433 -U GREE -P qwe!23 -d qianduantest -C -Q "select top 50 id, step_name, log_level, log_content, created_at from process_log order by id desc"
```

查工作流运行记录：

```cmd
sqlcmd -S 127.0.0.1,1433 -U GREE -P qwe!23 -d qianduantest -C -Q "select top 50 * from workflow_log order by id desc"
```

> 现场库若缺 `process_log` 表，日志会静默丢失，需先建表。

### 7.2 数据回传

在工控机上放一个定时任务，把关键现场状态导出到共享目录或 U 盘：

```cmd
sqlcmd -S 127.0.0.1,1433 -U GREE -P qwe!23 -d qianduantest -C -o "D:\field_export\process_log.txt" -Q "select top 200 * from process_log order by id desc"
robocopy "D:\field_export" "\\192.168.1.100\share\field_export" /E
```

### 7.3 缩短迭代周期

每次跑完把这几样一起带回来，一次就能定位大部分问题：

- `process_log` 最近 200 条
- `workflow_log` 最近 50 条
- `config.json`（现场实际生效的那份）
- `subs/` 目录下各子程序 exe 的修改时间（确认现场跑的是不是最新构建）
