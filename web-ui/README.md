# Roders数控机床数据采集与生产监控系统

基于 .NET Framework 4.7.2 的数控机床数据采集与生产监控系统，支持西门子、罗德斯等多种机床的 OPC UA 通信，包含完整的电极/刀具管理、料架管理、工作流调度功能。

## 技术栈

| 层 | 技术 |
|------|------|
| 后端 | C# .NET Framework 4.7.2 |
| 前端 | 原生 HTML + CSS + JavaScript |
| 数据库 | MySQL / SQL Server |
| 通信 | OPC UA (Siemens/Roders)、FOCAS (Fanuc)、HostLink (Omron) |

## 项目结构

```
web-ui/                    主项目
  frontend/               前端代码
    index.html            页面入口
    script.js             前端逻辑
    styles.css            样式
    *.png                 页面图片
  backend/                后端 C# 源码
    Program.cs            主程序（HTTP服务 + API + 工作流）
    MachineMonitor.csproj  项目文件
    config.json           系统配置
    bin/.../net472/       编译输出（运行目录）
  lib/                    依赖 DLL
  change_machine/         上机子程序
  load_machine/           装料子程序
  unload_machine/         卸料子程序
  prepare_material/       备料子程序
  scan/                   扫描子程序
  rackoperate/            料架操作子程序
  change_knife/           换刀子程序
  prepare_knife/          备刀子程序
  sendkniferequest/       发送刀具请求子程序
  unload_material/        卸料子程序
  deploy.bat              部署脚本
```

## 快速开始

### 启动服务

```bash
cd web-ui\backend\bin\Any CPU\Debug\net472
MachineMonitor.exe
```

服务默认监听 http://localhost:8888/

### 部署前端

修改前端文件后，运行部署脚本同步到运行目录：

```bash
web-ui\deploy.bat
```

### 编译

```bash
dotnet build web-ui\backend\MachineMonitor.csproj -c Debug
```

## API 接口

| 接口 | 方法 | 说明 |
|------|------|------|
| /api/dashboard | GET | 获取仪表盘数据（机床状态、产量等） |
| /api/config | GET/POST | 获取/保存系统配置 |
| /api/logs | GET | 获取日志 |
| /api/workflow/status | GET | 获取工作流状态 |
| /api/workflow/start | POST | 启动工作流 |
| /api/workflow/stop | POST | 停止工作流 |
| /api/workflow/pause | POST | 暂停工作流 |
| /api/workflow/resume | POST | 恢复工作流 |
| /api/machine/{action} | POST | 控制机床（启动/停止/复位） |
| /api/rack/status | GET | 获取料架状态 |
| /api/rack/create | POST | 创建料架 |
| /api/machine/create | POST | 创建机床 |
| /api/sub/log | POST | 子程序日志上报 |

## 工作流

系统支持可配置的工作流调度，工作流步骤包括：
- 上机 (load_machine) - 将工件装载到机床工位
- 卸料 (unload_material) - 从机床卸料
- 备料 (prepare_material) - 准备工件材料

每个步骤可启用/禁用，按优先级顺序执行。

## 数据库

支持 MySQL 和 SQL Server 两种数据库，自动建表。主要表包括：
- rack_administrate - 料架管理
- robotmessage - 机器人消息
- process_log - 流程日志
- workflow_step - 工作流步骤
- work_flow / process_flow - 工作流数据

## 版本历史

| 标签 | 说明 |
|------|------|
| 阶段0-基线 | 初始基线 |
| 阶段1-OPCUA配置链路修复 | 修复 OPC UA 会话 URL、用户名、密码配置链路 |
| 阶段2-前端配置显示修复 | 修复前端配置页机床显示 |
| 阶段3-子程序数据库修复 | 修复子程序 MySQL 连接串与 robotmessage 字段 |
| 阶段4-项目结构整理 | 统一前端目录、精简后端、移除无关文件 |