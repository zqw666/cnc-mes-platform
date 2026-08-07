// app.js - Vue 2 主界面（完整版）
new Vue({
  el: '#app',
  data: {
    currentSection: 'home',
    datetime: '',
    homeLog: [],
    workflowState: 'idle',
    machineStatus: '正常',
    robotStatus: '正常',
    shelfStatus: '自动',
    partName: '—',
    processDuration: '00:00:00',
    processedCount: 0,
    unprocessedCount: 0,
    pollTimer: null,
    wfTimer: null,
    logTimer: null,
    taskData: [],
    taskFilteredData: [],
    taskCurrentPage: 1,
    taskFilterTool: '',
    taskFilterMold: '',
    shelfTabs: [
      { key: 'electrode', label: '电极料架' },
      { key: 'tool', label: '刀具料架' },
      { key: 'steel', label: '钢件料架' },
      { key: 'pallet', label: '托盘料架' }
    ],
    shelfActiveTab: 'electrode',
    shelves: { electrode: [], tool: [], steel: [], pallet: [] },
    logData: [],
    logFilteredData: [],
    logCurrentPage: 1,
    logStartTime: '',
    logEndTime: '',
    logTypeFilter: '',
    logKeyword: '',
    lastLogId: 0,
    lineConfigGroups: [
      { key: 'line', label: '线体配置' },
      { key: 'plc', label: 'PLC信号' },
      { key: 'workflow', label: '工作流' }
    ],
    lineConfigActiveGroup: 'line',
    lineConfig: {
      gripperType: 'fixed-single', grabMode: 'A-grab-B-place',
      rackType: 'fixed', rackCount: 1, rackLayers: 8, rackSlots: 10,
      palletEnabled: 'yes', palletSlots: 4
    },
    plcConfig: {
      ip: '', port: 502,
      signals: {
        robot: { photo: '', taskStart: '', taskDone: '', error: '', safePos: '', emergencyStop: '' },
        machine: { doorRequest1: '', doorStatus1: '', doorRequest2: '', doorStatus2: '', doorRequest3: '', doorStatus3: '', interlock: '' },
        pallet: { steelGripperPhoto: '', electrodeGripperPhoto: '', slotPhoto: '', grabTask: '', placeTask: '', photoStart: '' },
        preset: { photo: '', manual: '' },
        rotary: { rotate: '', zero: '', grabTask: '', placeTask: '', arrive: '' },
        cleaner: { photo: '', grabTask: '', placeTask: '' }
      }
    },
    racksConfig: [],
    machinesConfig: [],
    grippersConfig: [],
    workflowConfig: []
  },
