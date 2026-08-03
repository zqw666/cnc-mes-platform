# -*- coding: utf-8 -*-
import os

def esc(s):
    if s is None: return ''
    s = str(s).replace('\\', '\\\\').replace('{', '\\{').replace('}', '\\}')
    return s

def rtf_header():
    return ('{\\rtf1\\ansi\\deff0\n'
            '{\\fonttbl{\\f0\\fswiss Arial;}{\\f1\\fswiss Arial;}}\n'
            '{\\colortbl;\\red47\\green84\\blue150;\\red255\\green255\\blue255;\\red214\\green228\\blue240;\\red242\\green247\\blue252;}\n'
            '\\f0\\fs20\n')

def rtf_title(text):
    return '\\pard\\qc\\b\\fs28\\cf1 %s\\par\n' % esc(text)

def rtf_heading(text, level=1):
    fs = {1: '24', 2: '22', 3: '20'}.get(level, '20')
    return '\\pard\\b\\fs%s\\cf1 %s\\par\n' % (fs, esc(text))

def rtf_table_row(cells, bold=False, bg=None, widths=None):
    row = '\\trowd\\trgaph70\n'
    if widths:
        for w in widths:
            row += '\\cellx%d\n' % w
    for i, c in enumerate(cells):
        if bold: row += '\\b '
        if bg: row += '\\clcbpat%d ' % bg
        row += '\\pard\\intbl %s\\cell\n' % esc(c)
    row += '\\row\n'
    return row

def rtf_para(text, bold=False, indent=0):
    b = '\\b ' if bold else ''
    return '\\pard%s %s\\par\n' % (b, esc(text))

def write_flow_section(f, title, headers, rows):
    f.write(rtf_heading(title, 2))
    f.write('\\par\n')
    # 3 columns: step 500, method 3000, desc 6000 = 8750 total
    w = [500, 3500, 8750]
    f.write(rtf_table_row(headers, bold=True, bg=1, widths=w))
    for r in rows:
        is_sub = bool(r[0]) and str(r[0]).startswith(' ')
        bg = 3 if is_sub else None
        cells = [r[i] if i < len(r) else '' for i in range(3)]
        f.write(rtf_table_row(cells, bg=bg, widths=w))
    f.write('\\par\\par\n')

f = open(r'D:\前后端分离\web-ui\流程整理.rtf', 'w', encoding='utf-8')
f.write(rtf_header())
f.write(rtf_title('Web-UI 子程序流程整理'))

# ===== 概览 =====
f.write(rtf_heading('一、流程概览', 1))
f.write(rtf_para(''))
w = [1600, 1000, 3500, 700, 2000]
f.write(rtf_table_row(['程序名称','显示名','核心功能','PLC','数据库表'], bold=True, bg=1, widths=w))
ov = [
    ['load_machine','上机流程','从随行台/机器人取料放机床，传NC程序启动加工','5','macmessage, following_rack, robotmessage, macworkposstatus, rack'],
    ['unload_machine','下料流程','从机床取加工完成工件放到随行台','3','macmessage, following_rack, robotmessage'],
    ['change_machine','换料流程','固定双爪一次取旧放新 / 快换爪+旋转台多次加工换料','6+','macmessage, robotmessage, rack_administrate, macworkposstatus'],
    ['change_knife','换刀流程','MC1刀库依次旋转1~10号刀','4','无'],
    ['rackoperate','料架操作','多料架循环巡检(门控+光电扫描+DB更新)','10+','rack_administrate'],
    ['prepare_material','备料流程','从料架取待加工工件到机器人手爪/随行台','3','macmessage, following_rack, robotmessage, rack_administrate'],
    ['backend','后端主控','HTTP服务/配置管理/子进程调度/工作流控制','--','所有表'],
]
for r in ov:
    f.write(rtf_table_row(r, widths=w))
f.write('\\par\\par\n')

# ===== load_machine =====
f.write(rtf_heading('二、上机流程 (load_machine)', 1))
lm = [
    ('1','LoadConfig()','加载 config.json: 数据库/PLC/夹爪/机床/随行台配置'),
    ('2','InitMachines()','MacFactory.CreateInstance 创建各品牌机床通信实例'),
    ('3','PLC 连接','Plc.Connect(ip, port)'),
    ('4','Waitmaclist()','查 macmessage 表: status!=0, 机床idle(reset), workpiecename为空'),
    ('5','HasShuttle()','读 config.json line.shuttleExists 判断系统是否有随行台'),
    ('6','获取工件来源',''),
    (' 6a','有随行台','FollowHavePieceList() → 查 following_rack: posstatus=2, workstate=0'),
    (' 6b','无随行台','GetRobotMessagePieceMessage() → 查 robotmessage 表夹爪上工件名'),
    ('7','ChangeGripper()','读 robotmessage → 固定爪跳过 / 快换爪:放工件→放夹爪→抓目标爪'),
    ('','=== 有随行台分支 ===',''),
    ('8a','MachineHasPiece()','查 macmessage: 机床上已有工件 → 跳过该机床'),
    ('9a','ExecuteTask(随行台)','机器人从随行台料位取工件: 复位TaskStart→写DM→发start→等taskDone'),
    ('10a','Updatefollowing_Rack','清空随行台料位: posstatus=1'),
    ('11a','Updaterobotmessage','机器人手爪=有工件: workpiecestatus=2'),
    ('12a','OpenMachineDoorIfRoders','Roders: ConnectMac→等idle(reset)→Send_PDOPEN'),
    ('13a','ExecuteTask(机床)','放工件到机床: 复位start→写DM→发start→等taskDone'),
    ('14a','CloseMachineDoorIfRoders','Roders: Send_PDCLOSE'),
    ('15a','Updaterobotmessage','机器人手爪清空: workpiecestatus=1'),
    ('16a','UpdateMacWorkPosStatus','工位表: 填入工件名+待加工状态'),
    ('17a','HandleUploadProgram','判断工位数→需合并则先合并NC→transfer.RunTransferProcess→MacStartProcessSafe'),
    ('18a','UpdateMacWorkPosStatus','工位表: 更新为加工中(workpiecestatus=1)'),
    ('19a','Updaterack_administrate','料架表: 更新工件加工状态=1(加工中)'),
    ('','=== 无随行台分支 ===',''),
    ('8b','MachineHasPiece()','机床上已有工件 → 退出流程'),
    ('9b','ExecuteTask(机床)','机器人手上有工件直接放机床: 复位start→写DM→发start→等taskDone'),
    ('10b','HandleUploadProgram','传输NC→启动加工→更新状态'),
]
write_flow_section(f, '', ['步骤','功能/方法','详细说明'], lm)

# ===== unload_machine =====
f.write(rtf_heading('三、下料流程 (unload_machine)', 1))
ul = [
    ('1','LoadConfig()+InitMachines()','加载配置，创建机床通信实例'),
    ('2','PLC 连接','Plc.Connect(ip, port)'),
    ('3','Waitmaclist()','查 macmessage: idle(reset)+workpiecename非空+workpiecestate=2(加工完成)'),
    ('4','FollowKongWeiList()','查 following_rack: posstatus=1(空位)'),
    ('','=== 有机床+有空位 ===',''),
    ('5a','ChangeGripper()','置换夹爪为目标类型'),
    ('6a','ExecuteTask(机床号)','从机床取加工完成工件: 写DM→发start→等taskDone'),
    ('7a','UpdateMacMessage','清空机床上工件信息(workpiecename=NULL)'),
    ('8a','Updaterobotmessage','手爪=有工件, 加工完成状态'),
    ('9a','ExecuteTask(随行台空位)','放到随行台空位: 写DM→发start→等taskDone'),
    ('10a','Updatefollowing_Rack','随行台料位=有工件(posstatus=2)'),
    ('','=== 有机床+无空位 ===',''),
    ('5b','ExecuteTask(机床号)','只取料(先不下)，等待后续有空位时再处理'),
]
write_flow_section(f, '', ['步骤','功能/方法','详细说明'], ul)

# ===== change_machine =====
f.write(rtf_heading('四、换料流程 (change_machine)', 1))
cm = [
    ('1','LoadConfig()+InitMachines()','加载配置，创建机床通信实例'),
    ('2','PLC 连接+ChangeGripper()','连接PLC，置换夹爪'),
    ('3','RunChangePiece()','换料主流程入口'),
    ('4','Waitmaclist()','查 macmessage: idle(reset)+workpiecename非空+workpieceworkstate=1(完成)'),
    ('','=== 固定双爪+无随行台 ===',''),
    ('5a','IsMachineRoders','判断机床品牌是否为 Roders'),
    ('6a','OpenRodersDoor','Roders: ConnectMac→等reset→OpenDoor'),
    ('7a','ExecuteTask(macnum)','一次任务完成取旧+放新: 复位→写DM→发start→等taskDone'),
    ('8a','UpdateMachinePieceOnChangePair','取爪:有工件(完成)/放爪:空/机床工位:新工件(待加工)'),
    ('9a','MacCloseDoor','Roders关门'),
    ('10a','transfer.RunTransferProcess','传输NC程序到机床'),
    ('11a','MacStartProcessSafe','启动加工'),
    ('12a','UpdateMacMessage+Updaterack','更新机床/料架状态→加工中'),
    ('','=== 快换爪+多次旋转加工 ===',''),
    ('5b','Roders开门','IsMachineRoders+OpenRodersDoor: 连接+等idle+开门'),
    ('6b','ExecuteTask(取料)','从机床取加工完成工件→Updaterobotmessage'),
    ('7b','ExecuteTask(放旋转台)','放置工件到旋转台'),
    ('8b','TurntableReturnZero+RunTurntable','转台回零→读加工卡xlsx角度→RunTurntable旋转到目标角度'),
    ('9b','ExecuteTask(取旋转台)','从旋转台取回工件→Updaterobotmessage'),
    ('10b','ExecuteTask(放回机床)','将旋转后的工件放回机床工位'),
    ('11b','MacCloseDoor','Roders关门'),
    ('12b','transfer+MacStartProcess','传输NC程序→启动加工→更新状态=加工中'),
]
write_flow_section(f, '', ['步骤','功能/方法','详细说明'], cm)

# ===== change_knife =====
f.write(rtf_heading('五、换刀流程 (change_knife)', 1))
ck = [
    ('1','LoadConfig()','加载 config.json: 数据库/PLC配置'),
    ('2','PLC 连接','Plc.Connect(ip, port)'),
    ('3','RunMC1Magazine(1~10)','依次旋转1~10号刀，每把刀执行以下步骤'),
    (' 3a','检查MC1DoorClosed(101.01)','ReadWR: 刀库门关闭(信号=1)则跳过该刀'),
    (' 3b','复位旋转信号','WriteWR(MC1RotateStart=104.01, 0)'),
    (' 3c','写目标刀号','WriteDM(MC1TaskDM=200, toolNo)'),
    (' 3d','发送旋转开始','WriteWR(MC1RotateStart=104.01, 1)'),
    (' 3e','等待转到位','ReadWR(MC1RotateDone=105.00) 循环直到=1'),
    (' 3f','复位旋转信号','WriteWR(MC1RotateStart=104.01, 0)'),
]
write_flow_section(f, '', ['步骤','功能/方法','详细说明'], ck)

# ===== rackoperate =====
f.write(rtf_heading('六、料架操作 (rackoperate)', 1))
ro = [
    ('1','LoadConfig()','加载 config.json: 数据库/PLC/料架(Racks)配置'),
    ('2','PLC 连接','Plc.Connect(ip, port)'),
    ('3','Work()','多料架循环巡检(最多3轮)，每轮遍历所有料架'),
    ('','=== ProcessRack() 门关场景 ===',''),
    ('4a','ReadWR(DoorClosed)=1','门已关闭，等5秒看人工开门请求'),
    ('5a','检查OpenRequest','收到请求+机器人任务完成→允许开门'),
    ('6a','发送开门允许脉冲','WriteWR(OpenAllow,1)→等500ms→WriteWR(OpenAllow,0)'),
    ('7a','等门打开','ReadWR(DoorClosed)循环→直到=0'),
    ('8a','置位DoorFlag','WriteWR(DoorFlag, 1)'),
    ('9a','等门关闭','ReadWR(DoorClosed)循环→直到=1'),
    ('10a','复位DoorFlag','WriteWR(DoorFlag, 0)'),
    ('11a','ReadPhotoelectricSignals','各层DM: baseAddr → baseAddr+layerCount-1，记录变化层'),
    ('12a','UpdateScanStatus','更新 rack_administrate: scanstatus=0(待扫描), 通知UI刷新'),
    ('','=== ProcessRack() 门开场景 ===',''),
    ('4b','ReadWR(DoorClosed)=0','门已打开，持续等待门关闭'),
    ('5b','门关后','ReadPhotoelectricSignals→UpdateScanStatus'),
    ('','=== 转台控制(方法保留) ===',''),
    ('','TurntableReturnZero','转台回零: WriteWR(120.00,1)→等SignalAtZero(108.05)→复位'),
    ('','RunTurntable','旋转到角度: WriteWr(110)→WriteWR(108.00/108.01)→等MoveDone(108.04)'),
]
write_flow_section(f, '', ['步骤','功能/方法','详细说明'], ro)

# ===== prepare_material =====
f.write(rtf_heading('七、备料流程 (prepare_material)', 1))
pm = [
    ('1','LoadConfig()+InitMachines()','加载配置，创建机床实例'),
    ('2','PLC 连接','Plc.Connect(ip, port)'),
    ('3','Waitmaclist()','查 macmessage: idle(reset)+无工件'),
    ('4','FollowKongWeiList()','查 following_rack: posstatus=1(空位)'),
    ('5','FollowHavePieceList()','查 following_rack: posstatus=2(有料), workstate=0(待加工)'),
    ('6','GetRobotClampStatus()','固定爪→clampstatus=1 / 快换爪→读DM获取夹爪类型编号'),
    ('7','ChangeGripper()','根据 TargetGripperType 置换到目标夹爪'),
    ('','=== 固定爪+料架有工件 ===',''),
    ('8a','ExecuteTask(料架空位)','如果有手上工件→放到料架空位'),
    ('9a','ExecuteTask(料架工件)','从料架抓取待加工工件'),
    ('10a','Updaterobotmessage','手爪=有工件(待加工状态)'),
    ('','=== 快换爪+需换爪 ===',''),
    ('8b','放当前工件','ExecuteTask→放到料架空位→ClearRobotWorkpiece'),
    ('9b','放当前夹爪','ExecuteTask→PlaceTaskNo→ClearRobotClamp'),
    ('10b','抓取目标夹爪','ExecuteTask→GrabTaskNo→更新robotmessage表'),
]
write_flow_section(f, '', ['步骤','功能/方法','详细说明'], pm)

# ===== backend =====
f.write(rtf_heading('八、后端主控 (backend)', 1))
be = [
    ('1','启动','读取 config.json→连接数据库→初始化子进程管理器'),
    ('2','HTTP服务','监听 http://localhost:8888 处理所有请求'),
    ('','API 端点',''),
    (' 3a','GET  /api/config','返回完整 config.json(数据库/PLC/机床/料架/夹爪/工作流)'),
    (' 3b','POST /api/config','前端保存线体配置→更新 config.json→同步DB'),
    (' 3c','POST /api/sub/log','子进程日志回传→写入控制台+推送到UI'),
    (' 3d','POST /api/machine/create','创建机床→写 macmessage+macworkposstatus 表→更新config.json'),
    (' 3e','POST /api/rack/create','创建料架→写 rack_administrate 表→更新config.json racks节'),
    (' 3f','POST /api/workflow/start','启动工作流→依次 Process.Start 拉起各子进程exe'),
    (' 3g','POST /api/workflow/stop','停止工作流→创建 stop_*.flag 文件(子进程检测到则退出)'),
    ('','子进程调度',''),
    ('4','启动子进程','Process.Start(exe路径, "config.json路径")→子进程以命令行参数接收配置'),
    ('5','停止子进程','File.Create("stop_xxx.flag")→子进程 Main 循环中检测文件即退出'),
    ('6','日志收集','子进程 POST /api/sub/log → backend 写入控制台 + 推送 UI 实时显示'),
]
write_flow_section(f, '', ['步骤','功能/方法','详细说明'], be)

# ===== 关系图 =====
f.write(rtf_heading('九、流程关系图', 1))
f.write('\\par\n')
rel = [
    ('backend (主控)','HTTP:8888, 配置管理, 子进程调度'),
    ('    ├── prepare_material','备料: 料架→机器人手爪'),
    ('    ├── rackoperate','料架扫描: 门控+光电+DB'),
    ('    ├── change_knife','刀库旋转: MC1 1~10号刀'),
    ('    ├── load_machine','上机: 工件→机床→启动加工'),
    ('    ├── change_machine','换料: 取旧放新/旋转台'),
    ('    └── unload_machine','下料: 机床→随行台'),
]
f.write(rtf_para('主控调度关系:', bold=True))
for name, desc in rel:
    f.write(rtf_para('%s  %s' % (name, desc)))

f.write('\\par\n')
f.write(rtf_para('通信方式:', bold=True))
comm = [
    '子进程 → 主控: HTTP POST /api/sub/log (日志回传)',
    '主控 → 子进程: 命令行参数传递 config.json 路径',
    '主控 → 子进程(停止): 文件信号 stop_*.flag',
    '所有子进程共享: config.json (数据库/PLC/机床配置)',
    '所有子进程共享: MySQL/SQL Server 数据库',
]
for c in comm:
    f.write(rtf_para('  - ' + c))

f.write('\\par\n')
f.write('}\n')
f.close()

# Rename to .doc
os.rename(r'D:\前后端分离\web-ui\流程整理.rtf', r'D:\前后端分离\web-ui\流程整理.doc')
print('OK - 流程整理.doc')
