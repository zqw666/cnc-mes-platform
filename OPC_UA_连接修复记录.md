# OPC UA 连接问题排查与修复记录

## 问题描述

ERP 项目中西门子（Siemens）机床通过 OPC UA 协议通信时连接失败，`Siemensoperate.Connect()` 返回 `Connected=false`，导致无法读取机床状态和坐标数据。

## 根因分析
### 问题 4：config.json 末尾逗号导致 JSON 解析失败

`config.json` 中对象最后一行的末尾有多余逗号（trailing comma），例如：

```json
"password":  "SUNRISE",
                           },
```

`System.Web.Script.Serialization.JavaScriptSerializer` 不支持非标准 JSON 语法，遇到末尾逗号会抛出 `ArgumentException: 无效的 JSON 基元`。

修复：使用正则表达式 `,\\n(\\s+[}\\]])` 移除所有末尾逗号。


### 问题 1：config.json 缺少 OPC UA 凭据

`web-ui\\backend\\config.json` 中机床 1 的配置只有 `type`/`ip`/`port`/`ncPath`，**缺少 `username` 和 `password`**。

调用链路：

```
config.json -> InitMachines() -> MacFactory.CreateInstance(siemense, ip, sessionUrl, username, password)
    -> new Siemence(macip, sessionUrl, username, password, winscpKey)
        -> new Siemensoperate(IP, UserName, PassWord, WinScpHostKey, SessionUrl)
            -> Server.Connect(url, "", "")  // 用户名密码为空，服务器拒绝认证
```

由于 `username` 和 `password` 传入空字符串，OPC UA 服务器返回 `BadIdentityTokenRejected`，连接失败。

### 问题 2：Siemensoperate.Connect() 类型转换顺序

在 `Siemensoperate.cs` 的 `Connect()` 方法中：

```csharp
string[] nameSpaceArray = (string[])results[0].Value;  // 先转型
if (results[0].StatusCode.ToString() == "Good")          // 后检查状态
```

转型操作**先于**状态码检查。如果 `StatusCode` 不是 `"Good"`，`results[0].Value` 可能为 `null` 或其他类型，导致 `InvalidCastException` 或 `NullReferenceException`。异常被外层 `catch` 吞掉后，`Connected` 始终为 `false`。

### 问题 3：CreateMachine() 未保存凭据

后端 `CreateMachine()` 方法在写入 `config.json` 时只保存了 `type`/`ip`/`ncPath`/`slots` 四个字段，**没有保存 `username` 和 `password`**，导致每次通过页面添加西门子机床后，配置文件都缺少 OPC UA 凭据。

## 修复内容

### 1. config.json — 补充凭据

**文件**: `web-ui\\backend\\config.json`

在机床 1 的配置中增加：

```json
{
    "type": "siemense",
    "ip": "192.168.101.53",
    "username": "OpcUaClient",
    "password": "SUNRISE",
    ...
}
```

### 2. Siemensoperate.cs — 修复类型转换顺序

**文件**: `mac\\MacCommunicate\\MacCommunicate\\Siemensoperate.cs`

将状态码检查移到转型之前：

```csharp
// 修复前
string[] nameSpaceArray = (string[])results[0].Value;
if (results[0].StatusCode.ToString() == "Good")

// 修复后
if (results[0].StatusCode.ToString() == "Good")
{
    string[] nameSpaceArray = (string[])results[0].Value;
```

### 3. Program.cs — CreateMachine() 增加凭据保存

**文件**: `web-ui\\backend\\Program.cs`

- 从请求体中读取 `username` 和 `password` 参数
- 写入 `config.json` 的 machines 节点

```csharp
// 读取请求参数
string opcUsername = body.ContainsKey("username") ? body["username"].ToString() : "";
string opcPassword = body.ContainsKey("password") ? body["password"].ToString() : "";

// 写入 config.json
machines[$"{{macId}}"] = new Dictionary<string, object>
{
    {{ "type", brand }},
    {{ "ip", macIp }},
    {{ "ncPath", ncpath }},
    {{ "slots", slots }},
    {{ "username", opcUsername }},   // 新增
    {{ "password", opcPassword }}    // 新增
};
```

## 验证结果

### OPC UA 连接测试

使用 `OpcUaMachineTester` 测试 CNC-01（192.168.101.53，凭据 OpcUaClient/SUNRISE）：

```
连接成功；端点=opc.tcp://192.168.101.53:4840；SinumerikVarProvider namespace=2；耗时=1821 ms
节点读取成功=8，失败=0，总数=8
```

### 编译测试

| 项目 | 结果 |
|------|------|
| MacCommunicate.dll | 0 错误 |
| MachineMonitor.exe | 0 错误，0 警告 |

## 三台机床凭据汇总

| 机床 | IP | 用户名 | 密码 |
|------|------|--------|------|
| CNC-01 | 192.168.101.53 | OpcUaClient | SUNRISE |
| CNC-02 | 192.168.101.52 | OpcUaClient | SUNRISE |
| CNC-03 | 192.168.101.51 | OpcUaClient | 1234 |

## 相关文件路径

| 文件 | 说明 |
|------|------|
| `web-ui\\backend\\config.json` | 系统配置文件，包含机床、PLC、料架等配置 |
| `web-ui\\backend\\Program.cs` | 后端主程序，`CreateMachine()` 方法在 1816 行 |
| `mac\\MacCommunicate\\MacCommunicate\\Siemensoperate.cs` | 西门子 OPC UA 通信封装类 |
| `mac\\MacCommunicate\\MacCommunicate\\Siemence.cs` | 西门子机床接口实现类 |
| `mac\\MacCommunicate\\MacCommunicate\\Macfactory.cs` | 机床工厂类，按类型创建通信实例 |
| `../../GreeCmmSys-FINAL/OpcUaMachineTester` | OPC UA 连接测试工具（独立项目） |
