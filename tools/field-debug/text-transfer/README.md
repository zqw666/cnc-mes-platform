# 文字通道作战手册

> 现场约束：**无外网 + U 盘禁接 + 内部平台基本只能传文字**。
> 这三条同时成立时，唯一可用的通路就是**文字**。本目录把调试资产全部改造成文字可承载的形式。

---

## 一、先想清楚：网络不通 ≠ 不能调试

**最关键的一点：后端 UI 根本不需要网络。**

工控机上直接开浏览器访问 `http://localhost:8888/`，就是完整的配置界面 —— 因为
`Program.cs` 监听的就是 `localhost:8888`。所有界面操作、配置修改、流程启停，**在工控机本机就能做完**。

同理，运行日志不在文件里，而在数据库 `process_log` 表，用 `sqlcmd` 本机就能查。

所以"远程"这件事，只对**跨机器**操作才成立。先问自己：这次要做的，真的必须跨机器吗？

| 要做的事 | 需要网络吗 | 在哪做 |
|---|---|---|
| 操作配置界面、启停流程 | ❌ 不需要 | 工控机本机浏览器 `localhost:8888` |
| 查运行日志、看工作流状态 | ❌ 不需要 | 工控机本机 `sqlcmd` 查库 |
| 改 config.json | ❌ 不需要 | 工控机本机记事本 |
| 改代码后重新编译 | ❌ 不需要 | 工控机本机 `csc.exe`（系统自带） |
| 下断点调试 | ❌ 不需要 | 工控机本机跑 `msvsmon`，或直接看日志 |
| 在开发机上远程连过去 | ✅ 需要 | 只有这条真的需要网络 |

---

## 二、三条路径

### 路径 A：把闭环搬到工控机（最推荐）

既然人能在工控机前操作，就把"改码 → 编译 → 跑 → 看日志"整条链都放在工控机上：

1. **拿源码**：只需传改动的 `.cs` 文件（纯文本，几 KB），见 §三。
2. **编译**：`.NET Framework 自带的 csc.exe`，无需安装任何东西：

   ```
   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
   ```

   用 `field-diagnose.ps1` 先确认它存在（第 5 节）。
3. **运行**：直接跑 exe，或由 MachineMonitor 调度。
4. **看结果**：本机浏览器 + `sqlcmd` 查 `process_log`。

优点：不依赖任何文件传输，一轮闭环可能只要几分钟。

### 路径 B：base64 文字通道（传送文本资产）

任何文件都能转成 base64（**纯 ASCII 文本**），通过只允许文字的平台传过去，现场两行命令还原：

```powershell
$t = ((Get-Content '.\p.txt') | Where-Object { $_ -notmatch '^[#=]' }) -join ''
[IO.File]::WriteAllBytes('out.exe',[Convert]::FromBase64String($t))
```

> **为什么要按行过滤，而不是直接正则剔除非法字符**：
> base64 字符集包含全部英文字母和数字，如果用 `[^A-Za-z0-9+/=]` 过滤，
> 注释行里的文字（如 "field-diagnose.ps1"）会被当成 base64 内容保留，导致解码失败。
> 而 base64 里不含 `#`，尾部的 `=` 只出现在最后一片末尾，
> 所以按「行首是 `#` 或 `=`」剔除是安全且准确的。

适用：脚本、配置文件、源码、小体积 dll。
**不适用**：主程序 exe（几百 KB 起，base64 后体积膨胀 33%，分片太多不现实）。

现场要往外发文件时，用这一行生成文本：

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('D:\要发的文件')) | Set-Content out.txt
```

### 路径 C：补物理链路（唯一能真正远程的办法）

只要办公区到车间之间有一条网线 / 光纤 / 无线网桥，远程就复活了，之后按上一级 `README.md` 的 §四配置即可。
无外网不影响这条路 —— 它只是"两个局域网互连"，不需要互联网。

---

## 三、工具用法

| 文件 | 在哪跑 | 用途 |
|---|---|---|
| `pack.ps1` | 开发机 | 把文件转成 base64 分片文本（含还原命令和 SHA256） |
| `unpack.ps1` | 开发机 | 把现场回传的 base64 文本还原成文件并校验 |
| `field-diagnose.ps1` | **现场机** | 一键综合诊断，输出约 100 行报告，整段复制回传 |

### 在开发机打包一个文件

```powershell
powershell -ExecutionPolicy Bypass -File .\pack.ps1 -Path "D:\xxx\Program.cs"
```

会生成 `Program.cs.b64.txt`，里面已经写好现场还原命令，直接照抄即可。
文件较长时按 `PART n/N` 逐条粘贴到内部平台。

### 在现场取一份诊断报告

把 `field-diagnose.ps1` 用 base64 传进去还原后运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\field-diagnose.ps1
```

它会输出（同时存一份 txt）主机信息、网络、后端进程与端口、**关键配置**、
**编译环境**、数据库最近 15 条日志、`work_flow` 步骤、子程序文件时间、设备连通性。
把这段报告整段贴回来，基本就能定位问题方向。

如果平台单条消息放不下，只贴你关心的那几节（比如第 5、6、7 节）。

### 在现场只取日志

```cmd
sqlcmd -S 127.0.0.1,1433 -U GREE -P qwe!23 -d qianduantest -C -W -s "|" -Q "set nocount on; select top 30 id, log_level, left(log_content,90) as msg, convert(varchar(19),created_at,120) as t from process_log order by id desc"
```

调窄 `left(log_content,90)` 里的数字可以控制行宽，方便粘贴。

---

## 四、传输技巧

1. **先探平台的单条上限**。随便发一段长文本，看什么时候被截断，据此设置 `pack.ps1 -ChunkSize`。
2. **分片务必带序号**。`pack.ps1` 生成的 `PART 3/12` 标记就是为此，漏一片还原会失败。
3. **回传时带上 SHA256**。`pack.ps1` 和 `field-diagnose.ps1` 都会打印，`unpack.ps1 -ExpectSha256` 能立刻发现缺片。
4. **优先传文本源码，而不是编译产物**。源码可读、可 review、体积小、能现场编译（路径 A）。
5. **先在开发机本地验证还原命令**，别到了现场才发现命令写错。

---

## 五、注意事项

- `field-diagnose.ps1` 里的数据库口令是明文（`GREE/qwe!23`）。回传报告时若平台有外发审核，
  建议手动删掉该行，或运行前用 `-DbPass` 传空值跳过数据库检查。
- 复制粘贴时**尽量保留换行**：还原靠按行过滤，换行没了就要退回严格字符过滤兜底
  （`unpack.ps1` 内置这层兜底），可靠性会下降。
- 用 PowerShell 5.1 时，`.ps1` 文件**必须带 UTF-8 BOM**，否则中文会乱码。
  本目录所有脚本已带 BOM，自行编写时注意。
