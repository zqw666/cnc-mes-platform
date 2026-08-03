# -*- coding: utf-8 -*-
import zipfile, io, os

def esc(s):
    return str(s).replace('&','&amp;').replace('<','&lt;').replace('>','&gt;').replace('"','&quot;')

def cell(col, row, val, style=0):
    ref = '%s%d' % (chr(65+col), row)
    if style > 0:
        return '<c r="%s" s="%d" t="inlineStr"><is><t>%s</t></is></c>' % (ref, style, esc(val))
    return '<c r="%s" t="inlineStr"><is><t>%s</t></is></c>' % (ref, esc(val))

def row(cells):
    return '<row>%s</row>' % ''.join(cells)

SHEETS = {}

def add_sheet(name, headers, rows_data):
    SHEETS[name] = (headers, rows_data)

# ===== Overview =====
ov_h = ['程序名称', '显示名', '核心功能', 'PLC', '数据库表']
ov_r = [
    ['load_machine', '上机流程', '从随行台/机器人取料放机床，传NC启动加工', '5', 'macmessage,following_rack,robotmessage,macworkposstatus,rack'],
    ['unload_machine', '下料流程', '从机床取加工完成工件放到随行台', '3', 'macmessage,following_rack,robotmessage'],
    ['change_machine', '换料流程', '固定双爪取旧放新/快换爪+旋转台多次加工', '6+', 'macmessage,robotmessage,rack_administrate,macworkposstatus'],
    ['change_knife', '换刀流程', 'MC1刀库旋转1~10号刀', '4', '无'],
    ['rackoperate', '料架操作', '多料架循环巡检(门控+光电+DB)', '10+', 'rack_administrate'],
    ['prepare_material', '备料流程', '从料架取工件到机器人手爪', '3', 'macmessage,following_rack,robotmessage,rack_administrate'],
    ['backend', '后端主控', 'HTTP服务/配置管理/工作流调度', '--', '所有表'],
]
add_sheet('流程概览', ov_h, ov_r)

# ===== load_machine =====
lm = [
    ['1','LoadConfig()','加载 config.json: 数据库/PLC/夹爪/机床/随行台配置'],
    ['2','InitMachines()','MacFactory.CreateInstance 创建各品牌机床通信实例'],
    ['3','PLC 连接','Plc.Connect(ip, port)'],
    ['4','Waitmaclist()','查 macmessage: status!=0, idle(reset), 无工件'],
    ['5','HasShuttle()','读 config.json line.shuttleExists 判断有无随行台'],
    ['6','获取工件来源',''],
    ['6a','有随行台','FollowHavePieceList() → following_rack(posstatus=2,workstate=0)'],
    ['6b','无随行台','GetRobotMessagePieceMessage() → robotmessage 夹爪工件名'],
    ['7','ChangeGripper()','固定爪跳过/快换爪:放工件→放夹爪→抓目标爪'],
    ['','===有随行台===' ,''],
    ['8a','MachineHasPiece()','机床上已有工件 → 跳过'],
    ['9a','ExecuteTask(随行台)','从随行台取工件: 复位start→写DM→发start→等taskDone'],
    ['10a','Updatefollowing_Rack','清空随行台料位(posstatus=1)'],
    ['11a','Updaterobotmessage','手爪=有工件(workpiecestatus=2)'],
    ['12a','OpenMachineDoorIfRoders','Roders: ConnectMac→等reset→OpenDoor'],
    ['13a','ExecuteTask(机床)','放工件: 复位→写DM→发start→等taskDone'],
    ['14a','CloseMachineDoorIfRoders','Roders关门'],
    ['15a','UpdateMacWorkPosStatus','工位表: 填入工件+待加工'],
    ['16a','HandleUploadProgram','合并NC(多工位)→transfer→MacStartProcessSafe'],
    ['17a','Updaterack_administrate','料架表: 更新加工状态=1'],
    ['','===无随行台===' ,''],
    ['8b','MachineHasPiece()','机床有工件 → 退出'],
    ['9b','ExecuteTask(机床)','机器人直接放工件到机床'],
    ['10b','HandleUploadProgram','传NC→启动加工→更新状态'],
]
add_sheet('上机流程', ['步骤','功能/方法','详细说明'], lm)

# ===== unload_machine =====
ul = [
    ['1','LoadConfig+InitMachines','加载配置，创建机床实例'],
    ['2','PLC 连接','Plc.Connect(ip, port)'],
    ['3','Waitmaclist()','查 macmessage: idle+有工件名+workpiecestate=2'],
    ['4','FollowKongWeiList()','查 following_rack: posstatus=1(空位)'],
    ['','===有机床+有空位===',''],
    ['5','ChangeGripper()','置换夹爪'],
    ['6','ExecuteTask(机床)','从机床取加工完成工件'],
    ['7','UpdateMacMessage','清空机床上工件信息'],
    ['8','Updaterobotmessage','手爪=有工件(完成)'],
    ['9','ExecuteTask(随行台)','放到随行台空位'],
    ['10','Updatefollowing_Rack','随行台=有工件(posstatus=2)'],
]
add_sheet('下料流程', ['步骤','功能/方法','详细说明'], ul)

# ===== change_machine =====
cm = [
    ['1','LoadConfig+InitMachines','加载配置，创建机床实例'],
    ['2','ChangeGripper()','置换夹爪'],
    ['3','RunChangePiece()','换料主流程入口'],
    ['4','Waitmaclist()','查 macmessage: idle+工件+workstate=1(完成)'],
    ['','===固定双爪===',''],
    ['5','IsMachineRoders','判断品牌'],
    ['6','OpenRodersDoor','Roders: Connect+等reset+开门'],
    ['7','ExecuteTask(macnum)','一次任务取旧+放新'],
    ['8','UpdateMachinePieceOnChangePair','取爪有工件(完成)/放爪空/机床新工件(待加工)'],
    ['9','MacCloseDoor','Roders关门'],
    ['10','transfer.RunTransferProcess','传输NC程序'],
    ['11','MacStartProcessSafe','启动加工→更新状态=加工中'],
    ['','===快换爪+旋转===',''],
    ['5b','Roders开门/取料/放旋转台','从机床取件→放到旋转台'],
    ['6b','TurntableReturnZero+RunTurntable','转台回零→读加工卡角度→旋转'],
    ['7b','取旋转台/放回机床','取件→放回机床工位'],
    ['8b','传NC→启动加工→关门','transfer+MacStartProcess+更新状态'],
]
add_sheet('换料流程', ['步骤','功能/方法','详细说明'], cm)

# ===== change_knife =====
ck = [
    ['1','LoadConfig()','加载 config.json'],
    ['2','PLC 连接','Plc.Connect(ip, port)'],
    ['3','RunMC1Magazine(1~10)','依次旋转1~10号刀:'],
    ['3a','检查门关(101.01)','ReadWR→门关则跳过'],
    ['3b','复位旋转开始','WriteWR(104.01,0)'],
    ['3c','写刀号','WriteDM(200, toolNo)'],
    ['3d','发旋转开始','WriteWR(104.01,1)'],
    ['3e','等转到位(105.00)','循环ReadWR直到=1'],
    ['3f','复位信号','WriteWR(104.01,0)'],
]
add_sheet('换刀流程', ['步骤','功能/方法','详细说明'], ck)

# ===== rackoperate =====
ro = [
    ['1','LoadConfig()','加载 config.json'],
    ['2','PLC 连接','Plc.Connect(ip, port)'],
    ['3','Work()','多料架循环巡检(最多3轮)'],
    ['','===门关场景===',''],
    ['4','ReadWR(DoorClosed)=1','门关, 等5s看开门请求'],
    ['5','收到请求+机器人完成','发送开门允许脉冲(OpenAllow)'],
    ['6','等门开→等门关','循环ReadWR→DoorFlag脉冲→读光电'],
    ['7','ReadPhotoelectricSignals','各层DM: baseAddr→baseAddr+layers-1'],
    ['8','UpdateScanStatus','rack_administrate: scanstatus=0(待扫描)'],
    ['','===门开场景===',''],
    ['4b','ReadWR(DoorClosed)=0','持续等待门关闭'],
    ['5b','门关后','读光电→UpdateScanStatus'],
]
add_sheet('料架操作', ['步骤','功能/方法','详细说明'], ro)

# ===== prepare_material =====
pm = [
    ['1','LoadConfig+InitMachines','加载配置'],
    ['2','PLC 连接','Plc.Connect(ip, port)'],
    ['3','Waitmaclist()','idle+无工件'],
    ['4','FollowKongWeiList()','随行台空位(posstatus=1)'],
    ['5','FollowHavePieceList()','随行台有料(posstatus=2,workstate=0)'],
    ['6','ChangeGripper()','置换夹爪'],
    ['','===固定爪===',''],
    ['7','ExecuteTask(料架)','有手上工件→放料架→从料架取待加工件→手爪有工件'],
    ['','===快换爪===',''],
    ['7b','放工件→放夹爪→抓目标爪','ExecuteTask→ClearRobot→抓新夹爪'],
]
add_sheet('备料流程', ['步骤','功能/方法','详细说明'], pm)

# ===== backend =====
be = [
    ['1','启动','读config.json→连接DB→初始化子进程管理器'],
    ['2','HTTP服务','监听 http://localhost:8888'],
    ['','API',''],
    ['a','GET /api/config','返回完整config.json'],
    ['b','POST /api/config','保存线体配置→写config.json+同步DB'],
    ['c','POST /api/sub/log','子进程日志回传→UI推送'],
    ['d','POST /api/workflow/start','启动工作流→拉起子进程exe'],
    ['e','POST /api/workflow/stop','创建stop_*.flag→子进程检测退出'],
    ['','子进程',''],
    ['3','启动','Process.Start(exe, config.json路径)'],
    ['4','停止','File.Create(stop_xxx.flag)'],
    ['5','通信','子进程POST/api/sub/log; 共享config.json; 共享DB'],
]
add_sheet('后端主控', ['步骤','功能/方法','详细说明'], be)

# ===== 关系图 =====
rel = [
    ['','','backend(主控)','HTTP:8888/配置管理/子进程调度'],
    ['','├──','prepare_material','备料: 料架→机器人手爪'],
    ['','├──','rackoperate','料架扫描: 门控+光电+DB'],
    ['','├──','change_knife','刀库旋转: MC1 1~10号刀'],
    ['','├──','load_machine','上机: 工件→机床→启动加工'],
    ['','├──','change_machine','换料: 取旧放新/旋转台'],
    ['','└──','unload_machine','下料: 机床→随行台'],
    ['','','',''],
    ['通信','','子进程→主控','HTTP POST /api/sub/log'],
    ['通信','','主控→子进程','命令行参数 config.json'],
    ['通信','','主控→子进程(停止)','文件信号 stop_*.flag'],
    ['共享','','所有子进程','config.json (DB/PLC/机床配置)'],
    ['共享','','所有子进程','MySQL/SQL Server 数据库'],
]
add_sheet('流程关系图', ['类型','','程序','说明'], rel)

# ===== Generate XLSX =====
STYLES_XML = '''<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
<fonts count="4">
<font><sz val="10"/><name val="Arial"/></font>
<font><sz val="14"/><b/><color rgb="FF2F5496"/><name val="Arial"/></font>
<font><sz val="12"/><b/><color rgb="FFFFFFFF"/><name val="Arial"/></font>
<font><sz val="10"/><b/><color rgb="FF2F5496"/><name val="Arial"/></font>
</fonts>
<fills count="5">
<fill><patternFill patternType="none"/></fill>
<fill><patternFill patternType="gray125"/></fill>
<fill><patternFill patternType="solid"><fgColor rgb="FF2F5496"/></patternFill></fill>
<fill><patternFill patternType="solid"><fgColor rgb="FFD6E4F0"/></patternFill></fill>
<fill><patternFill patternType="solid"><fgColor rgb="FFF2F7FC"/></patternFill></fill>
</fills>
<borders count="1">
<border><left style="thin"/><right style="thin"/><top style="thin"/><bottom style="thin"/></border>
</borders>
<cellStyleXfs count="1"><xf numFmtId="0"/></cellStyleXfs>
<cellXfs count="8">
<xf numFmtId="0" fontId="0"/>
<xf numFmtId="0" fontId="1"/>
<xf numFmtId="0" fontId="2" fillId="2" borderId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
<xf numFmtId="0" fontId="0" fillId="3" borderId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
<xf numFmtId="0" fontId="0" borderId="0" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
<xf numFmtId="0" fontId="3" fillId="3" borderId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
<xf numFmtId="0" fontId="0" fillId="4" borderId="0" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
<xf numFmtId="0" fontId="3" borderId="0" applyFont="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf>
</cellXfs>
</styleSheet>'''

SHEET_XML_TMPL = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
<cols>%s</cols>
<sheetData>%s</sheetData>
</worksheet>'''

SHARED_STRINGS_XML = '<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="0" uniqueCount="0"/>'

buf = io.BytesIO()
with zipfile.ZipFile(buf, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('[Content_Types].xml',
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
        '<Default Extension="xml" ContentType="application/xml"/>'
        '<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>'
        '<Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>'
        '%s'
        '</Types>' % ''.join(
            '<Override PartName="/xl/worksheets/sheet%d.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'%(i+1)
            for i in range(len(SHEETS))))

    z.writestr('_rels/.rels',
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>'
        '</Relationships>')

    z.writestr('xl/_rels/workbook.xml.rels',
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>'
        '<Relationship Id="rIdSharedStrings" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>'
        '%s'
        '</Relationships>' % ''.join(
            '<Relationship Id="rIdSheet%d" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet%d.xml"/>'%(i+1,i+1)
            for i in range(len(SHEETS))))

    sheets_xml = '<sheets>%s</sheets>' % ''.join(
        '<sheet name="%s" sheetId="%d" r:id="rIdSheet%d"/>' % (esc(name), i+1, i+1)
        for i, name in enumerate(SHEETS.keys()))

    z.writestr('xl/workbook.xml',
        '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
        '%s</workbook>' % sheets_xml)

    z.writestr('xl/styles.xml', STYLES_XML)
    z.writestr('xl/sharedStrings.xml', SHARED_STRINGS_XML)

    for i, (name, (headers, rows)) in enumerate(SHEETS.items()):
        ncols = len(headers)
        cols_xml = '<col min="1" max="%d" width="%d" customWidth="1"/>' % (1, 8)
        if ncols >= 2:
            cols_xml += '<col min="2" max="2" width="42" customWidth="1"/>'
        if ncols >= 3:
            cols_xml += '<col min="3" max="3" width="64" customWidth="1"/>'
        if ncols >= 4:
            cols_xml += '<col min="4" max="4" width="8" customWidth="1"/>'
        if ncols >= 5:
            cols_xml += '<col min="5" max="5" width="50" customWidth="1"/>'

        xml_rows = []
        # title row
        xml_rows.append(row([cell(0, 1, name)]))
        # header row
        xml_rows.append(row([cell(j, 2, h, 2) for j, h in enumerate(headers)]))
        # data rows
        for ri, rdata in enumerate(rows):
            rn = ri + 3
            is_sub = not rdata[0] or str(rdata[0]).startswith(' ')
            s = 3 if is_sub else 4
            cells_xml = [cell(j, rn, rdata[j] if j < len(rdata) else '', s) for j in range(ncols)]
            xml_rows.append(row(cells_xml))

        z.writestr('xl/worksheets/sheet%d.xml' % (i+1),
            SHEET_XML_TMPL % (cols_xml, ''.join(xml_rows)))

path = r'D:\前后端分离\web-ui\流程整理.xlsx'
with open(path, 'wb') as f:
    f.write(buf.getvalue())
print('OK:', path)
