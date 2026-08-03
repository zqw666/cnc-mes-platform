import openpyxl
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side

wb = Workbook()

header_font = Font(name='Arial', bold=True, size=12, color='FFFFFF')
header_fill = PatternFill('solid', fgColor='2F5496')
sub_fill = PatternFill('solid', fgColor='D6E4F0')
sub_font = Font(name='Arial', bold=True, size=11)
body_font = Font(name='Arial', size=10)
title_font = Font(name='Arial', bold=True, size=14, color='2F5496')
thin_border = Border(
    left=Side(style='thin'), right=Side(style='thin'),
    top=Side(style='thin'), bottom=Side(style='thin')
)

def srow(ws, row, cols, font=None, fill=None):
    for c in range(1, cols+1):
        cell = ws.cell(row=row, column=c)
        cell.font = font or body_font
        if fill: cell.fill = fill
        cell.alignment = Alignment(vertical='top', wrap_text=True)
        cell.border = thin_border

def add_sheet(wb, title, flows):
    ws = wb.create_sheet(title)
    ws.column_dimensions['A'].width = 8
    ws.column_dimensions['B'].width = 42
    ws.column_dimensions['C'].width = 64
    ws.cell(row=1, column=1, value=title).font = title_font
    ws.merge_cells('A1:C1')
    for i, h in enumerate(['步骤', '功能/方法', '详细说明']):
        c = ws.cell(row=2, column=i+1, value=h)
        c.font = header_font; c.fill = header_fill
        c.alignment = Alignment(horizontal='center'); c.border = thin_border
    r = 3
    for item in flows:
        ws.cell(row=r, column=1, value=item[0])
        ws.cell(row=r, column=2, value=item[1])
        ws.cell(row=r, column=3, value=item[2])
        fill = sub_fill if item[0] and not str(item[0]).startswith(' ') and len(item[0])<=5 and item[0].replace('a','').replace('b','').replace('f','').isdigit() else None
        srow(ws, r, 3, fill=fill)
        r += 1

# ===== 概览 =====
ws0 = wb.active
ws0.title = '流程概览'
ws0.column_dimensions['A'].width = 22
ws0.column_dimensions['B'].width = 14
ws0.column_dimensions['C'].width = 52
ws0.column_dimensions['D'].width = 18
ws0.column_dimensions['E'].width = 32
ws0.cell(row=1, column=1, value='web-ui 子程序流程概览').font = title_font
ws0.merge_cells('A1:E1')
for i, h in enumerate(['程序名称', '显示名', '核心功能', 'PLC信号数', '数据库表']):
    c = ws0.cell(row=2, column=i+1, value=h)
    c.font = header_font; c.fill = header_fill
    c.alignment = Alignment(horizontal='center'); c.border = thin_border
overview = [
    ['load_machine', '上机流程', '从随行台/机器人取料放机床, 传NC程序启动加工', '5', 'macmessage,following_rack,robotmessage,macworkposstatus,rack_administrate'],
    ['unload_machine', '下料流程', '从机床取加工完成工件放置到随行台', '3', 'macmessage,following_rack,robotmessage'],
    ['change_machine', '换料流程', '固定双爪一次取旧放新 / 快换爪+旋转台多次加工换料', '6+', 'macmessage,robotmessage,rack_administrate,macworkposstatus'],
    ['change_knife', '换刀流程', 'MC1刀库依次旋转1~10号刀', '4', '无'],
    ['rackoperate', '料架操作', '多料架循环巡检(门控+光电扫描+DB更新)', '10+', 'rack_administrate'],
    ['prepare_material', '备料流程', '从料架取待加工工件放到机器人手爪/随行台', '3', 'macmessage,following_rack,robotmessage,rack_administrate'],
    ['backend', '后端主控', 'HTTP服务, 配置管理, 子进程调度, 工作流控制', '--', '所有表'],
]
for i, row in enumerate(overview):
    for j, v in enumerate(row):
        ws0.cell(row=i+3, column=j+1, value=v)
    srow(ws0, i+3, 5)
    if i % 2 == 0:
        for c in range(1, 6):
            ws0.cell(row=i+3, column=c).fill = PatternFill('solid', fgColor='F2F7FC')

# ===== load_machine =====
add_sheet(wb, '上机流程', [
    ('1', 'LoadConfig()', '加载 config.json: 数据库/PLC/夹爪/机床/随行台配置'),
    ('2', 'InitMachines()', '调用 MacFactory.CreateInstance 创建各品牌机床通信实例(fanuc/siemense/makino/roders)'),
    ('3', 'PLC 连接', 'Plc.Connect(ip, port)'),
    ('4', 'Waitmaclist()', '查 macmessage: status!=0, 机床idle(reset), workpiecename为空 → 待机机床列表'),
    ('5', 'HasShuttle()', '读 config.json line.shuttleExists, 判断系统是否有随行台'),
    ('6', '获取工件来源', ''),
    (' 6a', '有随行台', 'FollowHavePieceList() → 查 following_rack: posstatus=2(有料),workstate=0(待加工)'),
    (' 6b', '无随行台', 'GetRobotMessagePieceMessage() → 查 robotmessage 表夹爪上工件名称'),
    ('7', 'ChangeGripper()', '读 robotmessage → 固定爪跳过 / 快换爪:放工件→放夹爪→抓目标爪'),
    ('', '=== 有随行台分支 ===', ''),
    ('8a', '遍历机床×工件', '双重循环: 每个机床与每个随行台待上机工件依次配对处理'),
    ('9a', 'MachineHasPiece()', '查 macmessage: 机床上已有工件 → 跳过该机床'),
    ('10a', 'ExecuteTask(随行台)', '从随行台料位取工件: 复位TaskStart→写DM→发start→等taskDone'),
    ('11a', 'Updatefollowing_Rack', '清空随行台料位: posstatus=1'),
    ('12a', 'Updaterobotmessage', '机器人手爪=有工件: workpiecestatus=2'),
    ('13a', 'OpenMachineDoorIfRoders', 'Roders: ConnectMac→等idle(reset)→Send_PDOPEN'),
    ('14a', 'ExecuteTask(机床)', '放工件到机床: 复位start→写DM→发start→等taskDone'),
    ('15a', 'CloseMachineDoorIfRoders', 'Roders: Send_PDCLOSE'),
    ('16a', 'Updaterobotmessage', '机器人手爪清空: workpiecestatus=1'),
    ('17a', 'UpdateMacWorkPosStatus', '工位表: 填入工件名+待加工状态(posstatus=0)'),
    ('18a', 'HandleUploadProgram', '判断工位数→需合并则先合并NC→transfer.RunTransferProcess→MacStartProcessSafe'),
    ('19a', 'UpdateMacWorkPosStatus', '工位表: 更新为加工中(workpiecestatus=1)'),
    ('20a', 'Updaterack_administrate', '料架表: 更新工件加工状态=1(加工中)'),
    ('', '=== 无随行台分支 ===', ''),
    ('8b', 'MachineHasPiece()', '机床上已有工件 → 退出流程'),
    ('9b', '无工件+机器人有工件', '机器人手上的工件直接放机床'),
    ('10b', 'OpenMachineDoorIfRoders', 'Roders: ConnectMac→等reset→OpenDoor'),
    ('11b', 'ExecuteTask(机床)', '放工件: 复位start→写DM→发start→等taskDone'),
    ('12b', 'CloseMachineDoorIfRoders', 'Roders关门'),
    ('13b', 'UpdateMacWorkPosStatus', '更新工位表工件信息'),
    ('14b', 'HandleUploadProgram', '传输NC→启动加工'),
    ('15b', 'Updaterack_administrate', '更新加工状态=1'),
])

# ===== unload_machine =====
add_sheet(wb, '下料流程', [
    ('1', 'LoadConfig()+InitMachines()', '加载配置, 创建机床通信实例'),
    ('2', 'PLC 连接', 'Plc.Connect(ip, port)'),
    ('3', 'Waitmaclist()', '查 macmessage: idle(reset)+workpiecename非空+workpiecestate=2(加工完成)'),
    ('4', 'FollowKongWeiList()', '查 following_rack: posstatus=1(空位)'),
    ('', '=== 有机床+有空位 ===', ''),
    ('5a', 'ChangeGripper()', '置换夹爪为目标类型'),
    ('6a', 'ExecuteTask(机床号)', '从机床取加工完成工件: 写DM→发start→等taskDone'),
    ('7a', 'UpdateMacMessage', '清空机床上工件信息(workpiecename=NULL)'),
    ('8a', 'Updaterobotmessage', '手爪=有工件, 加工完成状态'),
    ('9a', 'ExecuteTask(随行台空位)', '放到随行台空位: 写DM→发start→等taskDone'),
    ('10a', 'Updatefollowing_Rack', '随行台料位=有工件(posstatus=2, workstate=2)'),
    ('', '=== 有机床+无空位 ===', ''),
    ('5b', 'ExecuteTask(机床号)', '只取料(先不下)，等待后续有空位时再处理'),
])

# ===== change_machine =====
add_sheet(wb, '换料流程', [
    ('1', 'LoadConfig()+InitMachines()', '加载配置, 创建机床通信实例'),
    ('2', 'PLC 连接+ChangeGripper()', '连接PLC, 置换夹爪'),
    ('3', 'RunChangePiece()', '换料主流程入口'),
    ('4', 'Waitmaclist()', '查 macmessage: idle(reset)+workpiecename非空+workpieceworkstate=1(完成)'),
    ('', '=== 固定双爪 + 无随行台 ===', ''),
    ('5a', 'IsMachineRoders', '判断机床品牌是否为 Roders'),
    ('6a', 'OpenRodersDoor', 'Roders: ConnectMac→等reset→OpenDoor'),
    ('7a', 'ExecuteTask(macnum)', '一次任务完成取旧+放新: 复位→写DM→发start→等taskDone'),
    ('8a', 'UpdateMachinePieceOnChangePair', '取爪:有工件(完成)/放爪:空/机床工位:新工件(待加工)'),
    ('9a', 'MacCloseDoor', 'Roders关门'),
    ('10a', 'transfer.RunTransferProcess', '传输NC程序到机床'),
    ('11a', 'MacStartProcessSafe', '启动加工'),
    ('12a', 'UpdateMacMessage+Updaterack', '更新机床/料架状态→加工中'),
    ('', '=== 快换爪 + 多次旋转加工 ===', ''),
    ('5b', 'IsMachineRoders+OpenRodersDoor', 'Roders: 连接+开门'),
    ('6b', 'ExecuteTask(取料)', '从机床取加工完成工件'),
    ('7b', 'Updaterobotmessage', '手爪=有工件(完成状态)'),
    ('8b', 'ExecuteTask(放旋转台)', '放置工件到旋转台'),
    ('9b', 'TurntableReturnZero+RunTurntable', '转台回零→读加工卡xlsx角度→RunTurntable旋转到目标角度'),
    ('10b', 'ExecuteTask(取旋转台)', '从旋转台取回工件'),
    ('11b', 'ExecuteTask(放回机床)', '将旋转后的工件放回机床工位'),
    ('12b', 'MacCloseDoor', 'Roders关门'),
    ('13b', 'transfer+MacStartProcess', '传输NC程序→启动加工→更新状态=加工中'),
])

# ===== change_knife =====
add_sheet(wb, '换刀流程', [
    ('1', 'LoadConfig()', '加载 config.json: 数据库/PLC配置'),
    ('2', 'PLC 连接', 'Plc.Connect(ip, port)'),
    ('3', 'RunMC1Magazine(1..10)', '依次旋转1~10号刀, 每把刀执行以下步骤:'),
    (' 3a', '检查MC1DoorClosed(101.01)', 'ReadWR: 刀库门关闭(信号=1)则跳过该刀'),
    (' 3b', '复位旋转信号', 'WriteWR(MC1RotateStart=104.01, 0)'),
    (' 3c', '写目标刀号', 'WriteDM(MC1TaskDM=200, toolNo)'),
    (' 3d', '发送旋转开始', 'WriteWR(MC1RotateStart=104.01, 1)'),
    (' 3e', '等待转到位', 'ReadWR(MC1RotateDone=105.00) → 循环直到=1'),
    (' 3f', '复位旋转信号', 'WriteWR(MC1RotateStart=104.01, 0)'),
])

# ===== rackoperate =====
add_sheet(wb, '料架操作', [
    ('1', 'LoadConfig()', '加载 config.json: 数据库/PLC/料架(Racks)配置'),
    ('2', 'PLC 连接', 'Plc.Connect(ip, port)'),
    ('3', 'Work()', '多料架循环巡检(最多3轮), 每轮遍历所有料架'),
    ('', 'ProcessRack() 门关场景:', ''),
    ('4a', 'ReadWR(DoorClosed)=1', '门已关闭, 等5秒看人工开门请求'),
    ('5a', '等5秒检查OpenRequest', '收到请求+机器人任务完成→允许开门'),
    ('6a', '发送开门允许脉冲', 'WriteWR(OpenAllow,1)→等500ms→WriteWR(OpenAllow,0)'),
    ('7a', '等门打开', 'ReadWR(DoorClosed)循环→直到=0'),
    ('8a', '置位DoorFlag', 'WriteWR(DoorFlag, 1)'),
    ('9a', '等门关闭', 'ReadWR(DoorClosed)循环→直到=1'),
    ('10a', '复位DoorFlag', 'WriteWR(DoorFlag, 0)'),
    ('11a', 'ReadPhotoelectricSignals', '各层DM: baseAddr → baseAddr+layerCount-1, 记录变化层'),
    ('12a', 'UpdateScanStatus', '更新 rack_administrate: scanstatus=0(待扫描), 通知UI刷新'),
    ('', 'ProcessRack() 门开场景:', ''),
    ('4b', 'ReadWR(DoorClosed)=0', '门已开, 持续等待门关闭'),
    ('5b', '门关后', 'ReadPhotoelectricSignals→UpdateScanStatus'),
    ('', '转台控制(方法保留)', 'TurntableReturnZero/RunTurntable 可供 change_machine 复用'),
])

# ===== prepare_material =====
add_sheet(wb, '备料流程', [
    ('1', 'LoadConfig()+InitMachines()', '加载配置, 创建机床实例'),
    ('2', 'PLC 连接', 'Plc.Connect(ip, port)'),
    ('3', 'Waitmaclist()', '查 macmessage: idle(reset)+无工件'),
    ('4', 'FollowKongWeiList()', '查 following_rack: posstatus=1(空位)'),
    ('5', 'FollowHavePieceList()', '查 following_rack: posstatus=2(有料),workstate=0(待加工)'),
    ('6', 'GetRobotClampStatus()', '固定爪→clampstatus=1 / 快换爪→读DM获取夹爪类型编号'),
    ('7', 'ChangeGripper()', '根据 TargetGripperType 置换到目标夹爪'),
    ('', '=== 固定爪+料架有工件 ===', ''),
    ('8a', 'ExecuteTask(料架空位)', '如果有手上工件→放到料架空位'),
    ('9a', 'ExecuteTask(料架工件)', '从料架抓取待加工工件'),
    ('10a', 'Updaterobotmessage', '手爪=有工件(待加工状态)'),
    ('', '=== 快换爪+需换爪 ===', ''),
    ('8b', '放当前工件', 'ExecuteTask→放到料架空位→ClearRobotWorkpiece'),
    ('9b', '放当前夹爪', 'ExecuteTask→PlaceTaskNo→ClearRobotClamp'),
    ('10b', '抓取目标夹爪', 'ExecuteTask→GrabTaskNo→更新robotmessage表'),
])

# ===== backend =====
add_sheet(wb, '后端主控', [
    ('1', '启动', '读取 config.json→连接数据库→初始化子进程管理器'),
    ('2', 'HTTP服务', '监听 http://localhost:8888 处理所有请求'),
    ('', 'API 端点', ''),
    (' 3a', 'GET  /api/config', '返回完整 config.json(数据库/PLC/机床/料架/夹爪/工作流)'),
    (' 3b', 'POST /api/config', '前端保存线体配置→更新 config.json→同步DB'),
    (' 3c', 'POST /api/sub/log', '子进程日志回传→写入控制台+推送到UI'),
    (' 3d', 'POST /api/machine/create', '创建机床→写 macmessage+macworkposstatus 表→更新config.json'),
    (' 3e', 'POST /api/rack/create', '创建料架→写 rack_administrate 表→更新config.json racks节'),
    (' 3f', 'POST /api/workflow/start', '启动工作流→依次 Process.Start 拉起各子进程exe'),
    (' 3g', 'POST /api/workflow/stop', '停止工作流→创建 stop_*.flag 文件(子进程检测到则退出)'),
    ('', '子进程调度', ''),
    ('4', '启动', 'Process.Start(exe路径, "config.json路径")→每个子进程以参数接收配置路径'),
    ('5', '停止', 'File.Create("stop_xxx.flag")→子进程 Main 循环中检测文件存在即退出'),
    ('6', '日志', '子进程 POST /api/sub/log → backend 写入控制台 + 推送 UI 实时显示'),
])

# ===== 关系图 =====
ws = wb.create_sheet('流程关系图')
ws.column_dimensions['A'].width = 8
ws.column_dimensions['B'].width = 8
ws.column_dimensions['C'].width = 22
ws.column_dimensions['D'].width = 48
ws.cell(row=1, column=1, value='流程关系图').font = title_font
ws.merge_cells('A1:D1')
rel = [
    ('', '', 'backend (主控)', 'HTTP:8888, 配置管理, 子进程调度'),
    ('', '├──', 'prepare_material', '备料: 料架→机器人手爪'),
    ('', '├──', 'rackoperate', '料架扫描: 门控+光电+DB'),
    ('', '├──', 'change_knife', '刀库旋转: MC1 1~10号刀'),
    ('', '├──', 'load_machine', '上机: 工件→机床→传NC→启动'),
    ('', '├──', 'change_machine', '换料: 取旧放新/旋转台'),
    ('', '└──', 'unload_machine', '下料: 机床→随行台'),
    ('', '', '', ''),
    ('通信', '', '子进程→主控', 'HTTP POST /api/sub/log'),
    ('通信', '', '主控→子进程', '命令行参数 config.json路径'),
    ('通信', '', '主控→子进程(停止)', '文件信号: stop_xxx.flag'),
    ('共享', '', '所有子进程', '读取 config.json (DB/PLC/机床)'),
    ('共享', '', '所有子进程', '写入 MySQL/SQL Server 数据库'),
]
for i, rd in enumerate(rel):
    for j, v in enumerate(rd):
        ws.cell(row=i+2, column=j+1, value=v)
    srow(ws, i+2, 4)
    if rd[1] and '├──' in str(rd[1]):
        ws.cell(row=i+2, column=3).font = Font(name='Arial', size=10, bold=True)
    if rd[2] and '子进程→主控' in str(rd[2]):
        ws.cell(row=i+2, column=2).fill = PatternFill('solid', fgColor='E2EFDA')
        ws.cell(row=i+2, column=3).fill = PatternFill('solid', fgColor='E2EFDA')
        ws.cell(row=i+2, column=4).fill = PatternFill('solid', fgColor='E2EFDA')

wb.save(r'D:\前后端分离\web-ui\流程整理.xlsx')
print('OK - saved to web-ui\\流程整理.xlsx')
