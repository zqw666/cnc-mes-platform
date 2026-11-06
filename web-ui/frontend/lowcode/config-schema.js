/* 系统参数结构定义（config.json）。由 config-schema.json 生成，改这里即可。
   注意：HomeController 只服务 js/css/html/图片，不服务 .json —— 放开 .json 会让 /config.json 被直接下载（里面有数据库密码）。*/
window.CONFIG_SCHEMA = {
  "version": 1,
  "updatedAt": "2026-11-04",
  "desc": "系统参数（config.json）的结构描述。来源是现场实际的 config.json，字段名必须与子程序读取的 key 完全一致，改名字会导致子程序读不到。",
  "fieldTypes": {
    "text": "任意文本",
    "number": "数字",
    "switch": "开关",
    "enum": "下拉",
    "ip": "IP 地址",
    "addr": "PLC 地址（位地址 505.09 / 数据区 D200 / 任务号 801）",
    "password": "敏感文本，默认隐藏",
    "path": "文件或共享路径"
  },
  "tabs": [
    {
      "key": "plc",
      "name": "PLC 连接",
      "kind": "object",
      "path": "plc",
      "desc": "欧姆龙 NX1P2，FINS/TCP",
      "fields": [
        {
          "name": "ip",
          "type": "ip",
          "label": "IP 地址",
          "required": true,
          "hint": "现场设备网段，例 192.168.101.10"
        },
        {
          "name": "port",
          "type": "number",
          "label": "端口",
          "default": 9600,
          "hint": "FINS/TCP 默认 9600"
        }
      ]
    },
    {
      "key": "signals",
      "name": "PLC 信号映射",
      "kind": "matrix",
      "path": "plc.signals",
      "desc": "地址写错不会报错，只会「没反应」或「动错地方」。填完点「保存」前会自动检查格式和重复占用。",
      "groups": [
        {
          "key": "robot",
          "name": "机器人",
          "fields": [
            {
              "name": "photo",
              "type": "addr",
              "label": "拍照到位"
            },
            {
              "name": "taskStart",
              "type": "addr",
              "label": "任务开始"
            },
            {
              "name": "taskDone",
              "type": "addr",
              "label": "任务完成"
            },
            {
              "name": "taskNumber",
              "type": "addr",
              "label": "任务号"
            },
            {
              "name": "error",
              "type": "addr",
              "label": "报警"
            },
            {
              "name": "safePos",
              "type": "addr",
              "label": "安全位"
            },
            {
              "name": "estop",
              "type": "addr",
              "label": "急停"
            }
          ]
        },
        {
          "key": "pallet",
          "name": "托盘",
          "fields": [
            {
              "name": "slotPhoto",
              "type": "addr",
              "label": "料位拍照"
            },
            {
              "name": "grabTaskNo",
              "type": "addr",
              "label": "抓取任务号"
            },
            {
              "name": "placeTaskNo",
              "type": "addr",
              "label": "放置任务号"
            },
            {
              "name": "photoStart",
              "type": "addr",
              "label": "拍照启动"
            }
          ]
        },
        {
          "key": "preset",
          "name": "预设台",
          "fields": [
            {
              "name": "photo",
              "type": "addr",
              "label": "拍照"
            },
            {
              "name": "manual",
              "type": "addr",
              "label": "手动模式"
            },
            {
              "name": "openRequest",
              "type": "addr",
              "label": "开门请求"
            },
            {
              "name": "openAllow",
              "type": "addr",
              "label": "允许开门"
            },
            {
              "name": "grabTaskNo",
              "type": "addr",
              "label": "抓取任务号"
            },
            {
              "name": "placeTaskNo",
              "type": "addr",
              "label": "放置任务号"
            },
            {
              "name": "scanTaskNo",
              "type": "addr",
              "label": "扫码任务号"
            },
            {
              "name": "returnHomeTaskNo",
              "type": "addr",
              "label": "回原点任务号"
            },
            {
              "name": "doorLock",
              "type": "addr",
              "label": "门锁"
            }
          ]
        },
        {
          "key": "rotary",
          "name": "旋转台",
          "fields": [
            {
              "name": "rotate",
              "type": "addr",
              "label": "旋转"
            },
            {
              "name": "zero",
              "type": "addr",
              "label": "回零"
            },
            {
              "name": "grabTaskNo",
              "type": "addr",
              "label": "抓取任务号"
            },
            {
              "name": "placeTaskNo",
              "type": "addr",
              "label": "放置任务号"
            },
            {
              "name": "arrive",
              "type": "addr",
              "label": "到位"
            }
          ]
        },
        {
          "key": "cleaner",
          "name": "清洗机",
          "fields": [
            {
              "name": "photo",
              "type": "addr",
              "label": "拍照"
            },
            {
              "name": "grabTaskNo",
              "type": "addr",
              "label": "抓取任务号"
            },
            {
              "name": "placeTaskNo",
              "type": "addr",
              "label": "放置任务号"
            }
          ]
        }
      ]
    },
    {
      "key": "machines",
      "name": "机床",
      "kind": "kvlist",
      "path": "machines",
      "desc": "键就是机床号，子程序按这个号去数据库 macmessage 表取状态。",
      "keyLabel": "机床号",
      "defaultItem": {
        "type": "siemense",
        "number": "1",
        "ip": "",
        "port": "4840",
        "stationCount": "4",
        "ncPath": "",
        "sessionUrl": "",
        "username": "OpcUaClient",
        "password": "",
        "hostKey": "",
        "grabStartTaskNo": "",
        "placeStartTaskNo": "",
        "aluminumGrabTaskNo": "",
        "aluminumPlaceTaskNo": "",
        "palletWorkNum": "1",
        "workCoords": "G56,G57,G54,G55",
        "startProcessSignal": ""
      },
      "fields": [
        {
          "name": "number",
          "type": "text",
          "label": "机床号"
        },
        {
          "name": "type",
          "type": "enum",
          "label": "类型",
          "options": [
            {
              "value": "siemense",
              "label": "西门子（SINUMERIK）"
            },
            {
              "value": "roders",
              "label": "罗德斯（Roeders）"
            },
            {
              "value": "fanuc",
              "label": "发那科（FANUC）"
            },
            {
              "value": "makino",
              "label": "牧野（Makino）"
            }
          ]
        },
        {
          "name": "ip",
          "type": "ip",
          "label": "IP 地址",
          "required": true
        },
        {
          "name": "port",
          "type": "text",
          "label": "端口",
          "hint": "OPC UA 一般 4840"
        },
        {
          "name": "stationCount",
          "type": "number",
          "label": "工位数"
        },
        {
          "name": "sessionUrl",
          "type": "text",
          "label": "OPC UA 会话地址",
          "hint": "opc.tcp://IP:4840"
        },
        {
          "name": "username",
          "type": "text",
          "label": "用户名"
        },
        {
          "name": "password",
          "type": "password",
          "label": "密码"
        },
        {
          "name": "ncPath",
          "type": "path",
          "label": "NC 程序目录",
          "hint": "机床上的路径，不是本机"
        },
        {
          "name": "hostKey",
          "type": "text",
          "label": "SSH HostKey",
          "hint": "SFTP 上传 NC 程序时校验用，换机器后必须更新"
        },
        {
          "name": "grabStartTaskNo",
          "type": "addr",
          "label": "抓取任务号"
        },
        {
          "name": "placeStartTaskNo",
          "type": "addr",
          "label": "放置任务号"
        },
        {
          "name": "aluminumGrabTaskNo",
          "type": "addr",
          "label": "铝板抓取任务号"
        },
        {
          "name": "aluminumPlaceTaskNo",
          "type": "addr",
          "label": "铝板放置任务号"
        },
        {
          "name": "palletWorkNum",
          "type": "text",
          "label": "托盘工位号"
        },
        {
          "name": "workCoords",
          "type": "text",
          "label": "工件坐标系",
          "hint": "逗号分隔，如 G56,G57,G54,G55"
        },
        {
          "name": "startProcessSignal",
          "type": "addr",
          "label": "启动加工信号"
        }
      ]
    },
    {
      "key": "racks",
      "name": "料架",
      "kind": "list",
      "path": "racks",
      "desc": "料架的开门、拍照、任务号信号。type 决定它在哪个流程里出现。",
      "titleField": "name",
      "defaultItem": {
        "id": "rack_1",
        "type": "electrode",
        "name": "料架1",
        "number": "1",
        "rows": "8",
        "cols": "10",
        "photoStartSignal": "",
        "photoResetSignal": "",
        "openRequest": "",
        "openSignal": "",
        "doorClosed": "",
        "doorFlag": "",
        "doorLock": "",
        "grabStartTaskNo": "",
        "placeStartTaskNo": "",
        "scanStartTaskNo": ""
      },
      "fields": [
        {
          "name": "id",
          "type": "text",
          "label": "标识",
          "hint": "程序内部用，别随便改"
        },
        {
          "name": "name",
          "type": "text",
          "label": "名称",
          "required": true
        },
        {
          "name": "type",
          "type": "enum",
          "label": "料架类型",
          "options": [
            {
              "value": "electrode",
              "label": "电极料架"
            },
            {
              "value": "aluminum",
              "label": "铝板电极料架"
            },
            {
              "value": "steel",
              "label": "钢件料架"
            }
          ]
        },
        {
          "name": "number",
          "type": "text",
          "label": "编号"
        },
        {
          "name": "rows",
          "type": "number",
          "label": "层数"
        },
        {
          "name": "cols",
          "type": "number",
          "label": "每层位"
        },
        {
          "name": "photoStartSignal",
          "type": "addr",
          "label": "拍照启动"
        },
        {
          "name": "photoResetSignal",
          "type": "addr",
          "label": "拍照复位"
        },
        {
          "name": "openRequest",
          "type": "addr",
          "label": "开门请求"
        },
        {
          "name": "openSignal",
          "type": "addr",
          "label": "开门信号"
        },
        {
          "name": "doorClosed",
          "type": "addr",
          "label": "门已关"
        },
        {
          "name": "doorFlag",
          "type": "addr",
          "label": "门标志"
        },
        {
          "name": "doorLock",
          "type": "addr",
          "label": "门锁"
        },
        {
          "name": "grabStartTaskNo",
          "type": "addr",
          "label": "抓取任务号"
        },
        {
          "name": "placeStartTaskNo",
          "type": "addr",
          "label": "放置任务号"
        },
        {
          "name": "scanStartTaskNo",
          "type": "addr",
          "label": "扫码任务号"
        }
      ]
    },
    {
      "key": "grippers",
      "name": "夹爪",
      "kind": "list",
      "path": "grippers",
      "desc": "夹爪类型必须与 line.gripperType 匹配 —— 配错了就会「用钢件夹爪去抓电极」，这是之前踩过的坑。",
      "titleField": "name",
      "defaultItem": {
        "id": "",
        "type": "electrode",
        "name": "夹爪1",
        "number": "1",
        "slotPhotoSignal": "",
        "grabTaskNo": "",
        "placeTaskNo": ""
      },
      "fields": [
        {
          "name": "id",
          "type": "text",
          "label": "标识",
          "hint": "程序内部用，别随便改"
        },
        {
          "name": "name",
          "type": "text",
          "label": "名称",
          "required": true
        },
        {
          "name": "type",
          "type": "enum",
          "label": "夹爪类型",
          "options": [
            {
              "value": "electrode",
              "label": "电极夹爪"
            },
            {
              "value": "steel",
              "label": "钢件夹爪"
            },
            {
              "value": "aluminum",
              "label": "铝板夹爪"
            }
          ]
        },
        {
          "name": "number",
          "type": "text",
          "label": "编号"
        },
        {
          "name": "slotPhotoSignal",
          "type": "addr",
          "label": "料位拍照信号"
        },
        {
          "name": "grabTaskNo",
          "type": "addr",
          "label": "抓取任务号"
        },
        {
          "name": "placeTaskNo",
          "type": "addr",
          "label": "放置任务号"
        }
      ]
    },
    {
      "key": "line",
      "name": "线体",
      "kind": "object",
      "path": "line",
      "desc": "整条线的运行模式。gripperType 与「夹爪」页签里的 type 必须对得上。",
      "fields": [
        {
          "name": "gripperType",
          "type": "enum",
          "label": "夹爪类型",
          "options": [
            {
              "value": "quick-electrode",
              "label": "快换-电极"
            },
            {
              "value": "fixed-single",
              "label": "固定-单爪"
            },
            {
              "value": "fixed-electron",
              "label": "固定-电极"
            }
          ],
          "hint": "决定子程序用哪套夹爪动作逻辑"
        },
        {
          "name": "grabMode",
          "type": "enum",
          "label": "抓取模式",
          "options": [
            {
              "value": "A-grab-B-place",
              "label": "A 抓 B 放"
            }
          ]
        },
        {
          "name": "palletEnabled",
          "type": "switch",
          "label": "启用托盘"
        },
        {
          "name": "palletSlots",
          "type": "number",
          "label": "托盘料位数"
        },
        {
          "name": "rackType",
          "type": "enum",
          "label": "料架制式",
          "options": [
            {
              "value": "fixed",
              "label": "固定料架"
            },
            {
              "value": "electrode",
              "label": "电极料架"
            },
            {
              "value": "aluminum",
              "label": "铝板料架"
            },
            {
              "value": "steel",
              "label": "钢件料架"
            }
          ]
        },
        {
          "name": "rackCount",
          "type": "number",
          "label": "料架数量"
        },
        {
          "name": "rackLayers",
          "type": "number",
          "label": "料架层数"
        },
        {
          "name": "rackSlots",
          "type": "number",
          "label": "每层料位数"
        }
      ]
    },
    {
      "key": "scanner",
      "name": "扫码器",
      "kind": "object",
      "path": "scanner",
      "desc": "RFID / 扫码枪",
      "fields": [
        {
          "name": "type",
          "type": "enum",
          "label": "类型",
          "options": [
            {
              "value": "rcd",
              "label": "RCD（网口）"
            },
            {
              "value": "rfid",
              "label": "RFID（串口）"
            }
          ]
        },
        {
          "name": "rcdIp",
          "type": "ip",
          "label": "RCD IP"
        },
        {
          "name": "rcdPort",
          "type": "text",
          "label": "RCD 端口"
        },
        {
          "name": "rfidCom",
          "type": "text",
          "label": "RFID 串口",
          "hint": "如 COM3"
        },
        {
          "name": "heartbeatInterval",
          "type": "number",
          "label": "心跳间隔",
          "unit": "秒"
        }
      ]
    },
    {
      "key": "eman",
      "name": "上游生产系统",
      "kind": "object",
      "path": "eman",
      "desc": "扫码子程序靠它查 verifyID 和工件名。连不上会导致扫码流程直接失败。",
      "fields": [
        {
          "name": "type",
          "type": "enum",
          "label": "数据库类型",
          "options": [
            {
              "value": "sqlserver",
              "label": "SQL Server"
            },
            {
              "value": "mysql",
              "label": "MySQL"
            }
          ]
        },
        {
          "name": "host",
          "type": "ip",
          "label": "主机",
          "required": true
        },
        {
          "name": "port",
          "type": "number",
          "label": "端口"
        },
        {
          "name": "database",
          "type": "text",
          "label": "库名"
        },
        {
          "name": "user",
          "type": "text",
          "label": "用户名"
        },
        {
          "name": "password",
          "type": "password",
          "label": "密码"
        }
      ]
    },
    {
      "key": "database",
      "name": "本机数据库",
      "kind": "object",
      "path": "database",
      "desc": "主程序与子程序共用的业务库（macmessage / 料位 / 工件表）。",
      "fields": [
        {
          "name": "type",
          "type": "enum",
          "label": "数据库类型",
          "options": [
            {
              "value": "sqlserver",
              "label": "SQL Server"
            },
            {
              "value": "mysql",
              "label": "MySQL"
            }
          ]
        },
        {
          "name": "host",
          "type": "text",
          "label": "主机"
        },
        {
          "name": "port",
          "type": "number",
          "label": "端口"
        },
        {
          "name": "database",
          "type": "text",
          "label": "库名"
        },
        {
          "name": "user",
          "type": "text",
          "label": "用户名"
        },
        {
          "name": "password",
          "type": "password",
          "label": "密码"
        }
      ]
    },
    {
      "key": "nc_server",
      "name": "NC 程序服务器",
      "kind": "object",
      "path": "nc_server",
      "desc": "加工程序的上传/下载来源",
      "fields": [
        {
          "name": "root_path",
          "type": "path",
          "label": "共享根目录",
          "hint": "如 \\\\192.168.1.100\\share"
        },
        {
          "name": "local_temp",
          "type": "path",
          "label": "本地临时目录"
        },
        {
          "name": "process_card_nc_line",
          "type": "number",
          "label": "产线号"
        },
        {
          "name": "process_card_nc_cell",
          "type": "text",
          "label": "单元格"
        }
      ]
    },
    {
      "key": "system",
      "name": "其他",
      "kind": "object",
      "path": "",
      "desc": "顶层散项",
      "fields": [
        {
          "name": "serverIp",
          "type": "ip",
          "label": "本机服务 IP",
          "hint": "工控机在上位网的地址，前端用它访问"
        },
        {
          "name": "log.maxDays",
          "type": "number",
          "label": "日志保留天数"
        }
      ]
    }
  ]
};
