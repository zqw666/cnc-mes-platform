// app.js — Vue 2 主界面（阶段1: home 板块 + 骨架）
new Vue({
  el: '#app',
  data: {
    currentSection: 'home',
    sidebarCollapsed: false,
    username: '示例用户',
    datetime: '',
    homeLog: [],
    machineStatus: '正常',
    robotStatus: '正常',
    shelfStatus: '自动',
    partName: '—',
    processDuration: '00:00:00',
    processedCount: 0,
    unprocessedCount: 0,
    workflowState: 'idle',
    pollTimer: null,
    wfTimer: null,
    logTimer: null,
    clockTimer: null,
    // 日志查询板块
    LOG_PAGE_SIZE: 50,
    logData: [],
    logFilteredData: [],
    logCurrentPage: 1,
    logStartTime: '',
    logEndTime: '',
    logTypeFilter: '',
    logKeyword: '',
    // 任务管理板块
    taskData: [],
    taskFilteredData: [],
    taskCurrentPage: 1,
    taskFilterTool: '',
    taskFilterMold: '',
    // 料架管理板块
    shelfTypes: [
      { key: 'electrode', label: '电极料架' },
      { key: 'tool', label: '刀具料架' },
      { key: 'steel', label: '钢件料架' },
      { key: 'pallet', label: '托盘料架' }
    ],
    shelfActiveTab: 'electrode',
    shelves: { electrode: [], tool: [], steel: [], pallet: [] },
    shelfRacktypeMap: { electrode: 1, tool: 3, steel: 2, pallet: 4 },
    shelfDefaultSlots: { electrode: 80, tool: 20, steel: 12, pallet: 16 },
    // 线体配置板块
    lineConfig: {
      rackType: 'fixed',
      rackCount: 1,
      rackLayers: 3,
      rackSlots: 6,
      gripperType: 'fixed-single',
      grabMode: 'A-grab-B-place',
      palletEnabled: false,
      palletSlots: 4
    },
    plcConfig: { ip: '', port: 9600 },
    plcSignals: {
      robot: { photo: '', taskStart: '', taskDone: '', taskNumber: '', error: '', safePos: '', estop: '' },
      pallet: { slotPhoto: '', grabTaskNo: '', placeTaskNo: '', photoStart: '' },
      preset: { photo: '', manual: '', openRequest: '', openAllow: '', grabTaskNo: '', placeTaskNo: '', doorLock: '' },
      rotary: { rotate: '', zero: '', grabTaskNo: '', placeTaskNo: '', arrive: '' },
      cleaner: { photo: '', grabTaskNo: '', placeTaskNo: '' }
    },
    racksConfig: [],
    machinesConfig: [],
    grippersConfig: [],
    workflowConfig: []
  },
  computed: {
    logPages() {
      return Math.max(1, Math.ceil(this.logFilteredData.length / this.LOG_PAGE_SIZE));
    },
    logStartIdx() { return (this.logCurrentPage - 1) * this.LOG_PAGE_SIZE; },
    logPagedData() {
      return this.logFilteredData.slice(this.logStartIdx, this.logStartIdx + this.LOG_PAGE_SIZE);
    },
    taskPages() { return Math.max(1, Math.ceil(this.taskFilteredData.length / 50)); },
    taskStartIdx() { return (this.taskCurrentPage - 1) * 50; },
    taskPagedData() { return this.taskFilteredData.slice(this.taskStartIdx, this.taskStartIdx + 50); },
    taskAllCurrentChecked() {
      var page = this.taskPagedData;
      return page.length > 0 && page.every(function(r) { return r.selected; });
    },
    isWorkflowRunning() { return this.workflowState === 'running'; },
    isWorkflowPaused() { return this.workflowState === 'paused'; },
    isWorkflowIdle() { return this.workflowState === 'idle'; },
    showGrabMode() { return this.lineConfig.gripperType === 'fixed-double'; },
    showGripperSection() { return (this.lineConfig.gripperType || '').indexOf('quick-') === 0; },
    gripperTypeName() {
      var map = { 'fixed-single': '固定单爪', 'fixed-double': '固定双爪', 'quick-electrode': '快换爪（电极）', 'quick-tool': '快换爪（刀具）', 'quick-pallet': '快换爪（托盘）' };
      return map[this.lineConfig.gripperType] || '固定单爪';
    }
  },
  methods: {
    pad(n) { return n < 10 ? '0' + n : '' + n; },
    statusClass(status) {
      var map = { '正常': 'normal', '运行': 'running', '暂停': 'paused', '停止': 'stopped', '自动': 'auto' };
      return map[status] || 'normal';
    },
    toast(msg, type) {
      if (window.ELEMENT && ELEMENT.Message) ELEMENT.Message({ message: msg, type: type || 'info', duration: 2500 });
      else console.log('[' + type + ']', msg);
    },
    switchSection(s) {
      this.currentSection = s;
      if (s === 'logs' && this.logData.length === 0) this.loadLogs();
      if (s === 'shelves') this.loadShelves();
      if (s === 'lineconfig') this.loadLineConfig();
    },
    updateTime() {
      var now = new Date();
      this.datetime = now.getFullYear() + '-' + this.pad(now.getMonth() + 1) + '-' + this.pad(now.getDate()) +
        ' ' + this.pad(now.getHours()) + ':' + this.pad(now.getMinutes()) + ':' + this.pad(now.getSeconds());
    },
    toggleSidebar() {
      this.sidebarCollapsed = !this.sidebarCollapsed;
      var sidebar = document.getElementById('sidebar');
      if (sidebar) sidebar.classList.toggle('collapsed');
      var main = document.getElementById('main');
      if (main) {
        var collapsed = sidebar ? sidebar.classList.contains('collapsed') : this.sidebarCollapsed;
        main.style.marginLeft = collapsed ? '60px' : '200px';
      }
    },
    async loadDashboard() {
      try {
        var res = await apiGet('/api/dashboard');
        if (!res.success) return;
        var d = res.data;
        this.partName = d.part_name || '—';
        this.processDuration = d.process_duration || '00:00:00';
        this.processedCount = d.today_processed || 0;
        this.unprocessedCount = d.today_unprocessed || 0;
        var m = (d.machines && d.machines[0]) || {};
        this.machineStatus = m.status === 1 ? '正常' : '停止';
      } catch (e) { /* silent */ }
    },
    async syncWorkflowStatus() {
      try {
        var res = await apiGet('/api/workflow/status');
        if (!res.success) return;
        this.workflowState = res.running ? (res.paused ? 'paused' : 'running') : 'idle';
        if (this.workflowState === 'running') {
          this.robotStatus = '运行';
          this.shelfStatus = '自动';
        } else if (this.workflowState === 'paused') {
          this.robotStatus = '暂停';
        } else {
          this.robotStatus = '正常';
          this.shelfStatus = '自动';
        }
      } catch (e) { /* silent */ }
    },
    async doWorkflowAction(action) {
      var labels = { start: '启动', pause: '暂停', stop: '停止', resume: '恢复' };
      try {
        var res = await apiPost('/api/workflow/' + action);
        if (res && res.success) {
          this.$message({ message: labels[action] + '命令已发送', type: 'success', duration: 2500 });
          await this.syncWorkflowStatus();
        } else {
          this.$message({ message: labels[action] + '失败: ' + ((res && res.message) || '未知错误'), type: 'warning', duration: 2500 });
        }
      } catch (e) {
        this.$message({ message: labels[action] + '失败', type: 'error', duration: 2500 });
      }
    },
    async loadHomeLog() {
      try {
        var res = await apiGet('/api/logs?page=1&pageSize=20');
        if (!res.success || !res.data || !res.data.logs) return;
        var logs = res.data.logs;
        if (logs.length === 0) return;
        var anyNew = false;
        for (var i = 0; i < logs.length; i++) {
          var log = logs[i];
          var content = log.content || log.message || '';
          var time = log.time || log.created_at || '';
          var exists = this.homeLog.some(function(h) { return h.content === content && h.time === time; });
          if (!exists) {
            this.homeLog.unshift({ time: time, content: content, type: log.level || log.type || 'info' });
            anyNew = true;
          }
        }
        if (anyNew && this.homeLog.length > 200) this.homeLog = this.homeLog.slice(0, 200);
      } catch (e) { /* silent */ }
    },
    clearHomeLog() { this.homeLog = []; },
    // ==================== 日志查询板块 ====================
    logTypeMap(type) {
      var map = { INFO: ['系统日志', 'type-system'], ROBOT: ['机器人日志', 'type-robot'], ERROR: ['错误日志', 'type-error'], WARN: ['警告日志', 'type-warn'], SUCCESS: ['成功日志', 'type-success'], DEBUG: ['调试日志', 'type-debug'] };
      var pair = map[type] || [type || 'INFO', ''];
      return { text: pair[0], cls: pair[1] };
    },
    async loadLogs() {
      try {
        var res = await apiGet('/api/logs?page=1&pageSize=500');
        if (!res.success || !res.data || !res.data.logs) return;
        var self = this;
        this.logData = res.data.logs.map(function(log, i) {
          var t = self.logTypeMap(log.level);
          return {
            id: i + 1,
            time: log.time || '',
            type: log.level || 'INFO',
            typeText: t.text,
            typeClass: t.cls,
            content: log.content || log.message || ''
          };
        });
        this.logFilteredData = this.logData.slice();
        this.logCurrentPage = 1;
      } catch (e) { /* silent */ }
    },
    applyLogFilter() {
      var sv = this.logStartTime ? new Date(this.logStartTime.replace('T', ' ')).getTime() : null;
      var ev = this.logEndTime ? new Date(this.logEndTime.replace('T', ' ')).getTime() : null;
      var tv = this.logTypeFilter;
      var kv = this.logKeyword.trim().toLowerCase();
      var self = this;
      this.logFilteredData = this.logData.filter(function(item) {
        var t = item.time ? new Date(item.time.replace(/-/g, '/')).getTime() : null;
        if (sv && (!t || t < sv)) return false;
        if (ev && (!t || t > ev)) return false;
        if (tv && item.type !== tv) return false;
        if (kv && !(item.content || '').toLowerCase().includes(kv)) return false;
        return true;
      });
      this.logCurrentPage = 1;
    },
    resetLogFilter() {
      this.logStartTime = '';
      this.logEndTime = '';
      this.logTypeFilter = '';
      this.logKeyword = '';
      this.logFilteredData = this.logData.slice();
      this.logCurrentPage = 1;
    },
    logGoPage(p) {
      if (p >= 1 && p <= this.logPages) this.logCurrentPage = p;
    },
    // ==================== 任务管理板块 ====================
    genTaskDemo() {
      var self = this;
      this.taskData = [];
      for (var i = 1; i <= 137; i++) {
        this.taskData.push({
          id: i,
          monId: 'MON-' + (1000 + i),
          partName: '零件-' + i,
          toolNo: i % 10 === 0 ? 'T-100' : 'T-' + (i % 20),
          mold: 'M-' + (i % 7 + 1),
          machine: '机台-' + (i % 5 + 1),
          selected: false
        });
      }
      this.taskFilteredData = this.taskData.slice();
      this.taskCurrentPage = 1;
    },
    applyTaskFilter() {
      var t = this.taskFilterTool.trim();
      var m = this.taskFilterMold.trim();
      var self = this;
      this.taskFilteredData = this.taskData.filter(function(item) {
        if (t && !String(item.toolNo).includes(t)) return false;
        if (m && !String(item.mold).includes(m)) return false;
        return true;
      });
      this.taskCurrentPage = 1;
    },
    taskToggleSelect(row) { row.selected = !row.selected; },
    taskSelectAllClick() {
      var self = this;
      this.taskPagedData.forEach(function(d) { d.selected = !self.taskAllCurrentChecked; });
    },
    taskSelectAllToggle() {
      var all = this.taskFilteredData.every(function(x) { return x.selected; });
      this.taskFilteredData.forEach(function(x) { x.selected = !all; });
    },
    topSelectedTasks() {
      var sel = this.taskFilteredData.filter(function(x) { return x.selected; });
      var rest = this.taskFilteredData.filter(function(x) { return !x.selected; });
      this.taskFilteredData = sel.concat(rest);
      this.taskCurrentPage = 1;
    },
    taskGoPage(p) {
      if (p >= 1 && p <= this.taskPages) this.taskCurrentPage = p;
    },
    // ==================== 料架管理板块 ====================
    shelfSwitchTab(key) { this.shelfActiveTab = key; },
    shelfFillDefault() {
      var self = this;
      this.shelfTypes.forEach(function(t) {
        var arr = [];
        for (var i = 1; i <= self.shelfDefaultSlots[t.key]; i++) {
          arr.push({ id: i, rfid: '', posstatus: 0, workstate: 0, racknum: 1, activated: false, selected: false });
        }
        self.shelves[t.key] = arr;
      });
    },
    async loadShelves() {
      var self = this;
      try {
        var res = await apiGet('/api/rack/status');
        if (!res.success) { this.shelfFillDefault(); return; }
        var allRows = Array.isArray(res.data && res.data.rack_administrate) ? res.data.rack_administrate : [];
        this.shelfTypes.forEach(function(t) {
          var rows = allRows.filter(function(r) {
            return (r.racktype || '').toString().toLowerCase() === t.key;
          }).sort(function(a, b) { return a.id - b.id; });
          var arr = [];
          for (var i = 1; i <= self.shelfDefaultSlots[t.key]; i++) {
            var row = rows[i - 1];
            arr.push(row ? {
              id: Number(row.id), rfid: row.rfid || '', posstatus: Number(row.posstatus || 0),
              workstate: Number(row.workstate || 0), scanstatus: Number(row.scanstatus || 0),
              priority: Number(row.priority || 0), racknum: Number(row.racknum || 1),
              activated: false, selected: false
            } : { id: i, rfid: '', posstatus: 0, workstate: 0, scanstatus: 0, priority: 0, racknum: 1, activated: false, selected: false });
          }
          self.shelves[t.key] = arr;
        });
      } catch (e) {
        console.warn('料架加载失败，使用默认空槽位', e);
        this.shelfFillDefault();
      }
    },
    shelfStatusOf(row, type) {
      if (type === 'steel') {
        if (row.scanstatus === 0) return 'to-scan';
        if (row.workstate === 1) return 'processing';
        if (row.workstate === 2) return 'completed';
        if (row.priority === 1) return 'urgent';
        if (row.posstatus === 1) return 'pending';
        return 'no-material';
      }
      if (row.workstate === 1) return 'processing';
      if (row.workstate === 2) return 'completed';
      if (row.posstatus === 1) return 'pending';
      return 'no-material';
    },
    getStatusText(status) {
      var map = { processing: '加工中', pending: '待加工', completed: '已完成', 'to-scan': '待扫描', 'no-material': '无料', error: '错误', disabled: '禁用', urgent: '急件' };
      return map[status] || status;
    },
    shelfToggleSelect(type, shelf) { shelf.selected = !shelf.selected; },
    shelfSelectAll(type) {
      this.shelves[type].filter(function(s) { return s.activated; }).forEach(function(s) { s.selected = true; });
    },
    shelfActivate(type) { this.shelves[type].forEach(function(s) { s.activated = true; }); },
    shelfLock(type) { this.shelves[type].forEach(function(s) { s.activated = false; }); },
    async shelfSetStatus(type, status) {
      var selected = this.shelves[type].filter(function(s) { return s.selected; });
      if (selected.length === 0) { this.toast('请先选择要操作的料位', 'warning'); return; }
      var ids = selected.map(function(s) { return s.id; });
      try {
        var res = await apiPost('/api/rack/status', { type: type, ids: ids, status: status });
        if (res.success) {
          var self = this;
          selected.forEach(function(s) {
            s.selected = false;
            if (type === 'steel') {
              if (status === 'no-material') s.posstatus = 0;
              else if (status === 'disable') { s.posstatus = 0; s.workstate = 0; s.scanstatus = 0; }
              else if (status === 'urgent') s.priority = 1;
              else if (status === 'activate') s.scanstatus = 1;
              else if (status === 'lock') s.scanstatus = 0;
            } else {
              if (status === 'no-material') s.posstatus = 0;
              else if (status === 'disable') { s.posstatus = 0; s.workstate = 0; }
              else if (status === 'activate') s.workstate = 1;
              else if (status === 'lock') s.workstate = 0;
            }
          });
          this.toast('状态已更新', 'success');
        } else { this.toast(res.message || '更新失败', 'error'); }
      } catch (e) { this.toast('更新失败: ' + e.message, 'error'); }
    },
    // ==================== 线体配置板块 ====================
    isSiemensMachine(m) {
      var t = (m.type || '').toLowerCase();
      return t === 'siemense' || t === 'siemens';
    },
    normalizeRack(r) {
      var rack = Object.assign({}, r);
      if (rack.type === '1') rack.type = 'electrode';
      else if (rack.type === '2') rack.type = 'steel';
      rack.openRequest = rack.openRequest || '';
      rack.openSignal = rack.openSignal || '';
      rack.doorClosed = rack.doorClosed || '';
      rack.doorFlag = rack.doorFlag || '';
      rack.grabStartTaskNo = rack.grabStartTaskNo || rack.grabTaskNo || '';
      rack.placeStartTaskNo = rack.placeStartTaskNo || rack.placeTaskNo || '';
      rack.scanStartTaskNo = rack.scanStartTaskNo || '';
      return rack;
    },
    async loadLineConfig() {
      try {
        // 优先使用 /api/config/raw 保留完整字段（含料架门/任务号等），workflow 来自 /api/config（数据库）
        var raw = null;
        try {
          var resRaw = await apiGet('/api/config/raw');
          if (resRaw && resRaw.success) raw = resRaw.data;
        } catch (e) { /* raw 不可用时回退到映射接口 */ }
        var mapped = null;
        try {
          var resMap = await apiGet('/api/config');
          if (resMap && resMap.success) mapped = resMap.data;
        } catch (e) { /* silent */ }

        var line = (raw && raw.line) || (mapped && mapped.line) || {};
        this.lineConfig = {
          rackType: line.rackType || 'fixed',
          rackCount: line.rackCount != null ? line.rackCount : 1,
          rackLayers: line.rackLayers != null ? line.rackLayers : 3,
          rackSlots: line.rackSlots != null ? line.rackSlots : 6,
          gripperType: line.gripperType || 'fixed-single',
          grabMode: line.grabMode || 'A-grab-B-place',
          palletEnabled: line.palletEnabled === true || line.palletEnabled === 'yes',
          palletSlots: line.palletSlots != null ? line.palletSlots : 4
        };

        if (raw) {
          // raw 结构：machines 是 dict，racks/grippers 是数组，字段完整
          var rawMachines = raw.machines || {};
          this.machinesConfig = Object.keys(rawMachines).map(function(id) {
            var m = rawMachines[id] || {};
            return {
              id: id, type: m.type || 'roders', number: m.number != null ? m.number : '1',
              ip: m.ip || '', port: m.port != null ? m.port : '', stationCount: m.stationCount != null ? m.stationCount : 4,
              ncPath: m.ncPath || '', sessionUrl: m.sessionUrl || '', username: m.username || '', password: m.password || ''
            };
          });
          this.racksConfig = Array.isArray(raw.racks) ? raw.racks.map(this.normalizeRack) : [];
          this.grippersConfig = Array.isArray(raw.grippers) ? raw.grippers.slice() : [];
        } else {
          this.machinesConfig = Array.isArray(line.machines) ? line.machines.slice() : [];
          this.racksConfig = Array.isArray(line.racks) ? line.racks.map(this.normalizeRack) : [];
          this.grippersConfig = Array.isArray(line.grippers) ? line.grippers.slice() : [];
        }
        if (this.racksConfig.length === 0) this.addRack();

        var plc = (raw && raw.plc) || (mapped && mapped.plc) || {};
        this.plcConfig = { ip: plc.ip || '', port: plc.port != null ? plc.port : 9600 };
        var signals = plc.signals || {};
        var robot = signals.robot || {};
        var pallet = signals.pallet || {};
        var preset = signals.preset || {};
        var rotary = signals.rotary || {};
        var cleaner = signals.cleaner || {};
        this.plcSignals = {
          robot: { photo: robot.photo || '', taskStart: robot.taskStart || '', taskDone: robot.taskDone || '', taskNumber: robot.taskNumber || '', error: robot.error || '', safePos: robot.safePos || '', estop: robot.estop || '' },
          pallet: { slotPhoto: pallet.slotPhoto || '', grabTaskNo: pallet.grabTaskNo || '', placeTaskNo: pallet.placeTaskNo || '', photoStart: pallet.photoStart || '' },
          preset: { photo: preset.photo || '', manual: preset.manual || '', openRequest: preset.openRequest || '', openAllow: preset.openAllow || '', grabTaskNo: preset.grabTaskNo || '', placeTaskNo: preset.placeTaskNo || '', doorLock: preset.doorLock || '' },
          rotary: { rotate: rotary.rotate || '', zero: rotary.zero || '', grabTaskNo: rotary.grabTaskNo || '', placeTaskNo: rotary.placeTaskNo || '', arrive: rotary.arrive || '' },
          cleaner: { photo: cleaner.photo || '', grabTaskNo: cleaner.grabTaskNo || '', placeTaskNo: cleaner.placeTaskNo || '' }
        };

        this.workflowConfig = (mapped && Array.isArray(mapped.workflow)) ? mapped.workflow.map(function(w) {
          return { id: w.id != null ? w.id : 0, name: w.name || '', enabled: w.enabled === true || w.enabled === 1, priority: w.priority != null ? w.priority : 0 };
        }) : [];
      } catch (e) {
        console.warn('线体配置加载失败', e);
      }
    },
    collectLineConfig() {
      return {
        line: {
          rackType: this.lineConfig.rackType,
          rackCount: parseInt(this.lineConfig.rackCount) || 1,
          rackLayers: parseInt(this.lineConfig.rackLayers) || 3,
          rackSlots: parseInt(this.lineConfig.rackSlots) || 6,
          gripperType: this.lineConfig.gripperType,
          grabMode: this.lineConfig.grabMode,
          palletEnabled: !!this.lineConfig.palletEnabled,
          palletSlots: parseInt(this.lineConfig.palletSlots) || 4
        },
        racks: this.racksConfig,
        machines: this.machinesConfig,
        grippers: this.grippersConfig,
        plc: {
          ip: this.plcConfig.ip,
          port: parseInt(this.plcConfig.port) || 9600,
          signals: JSON.parse(JSON.stringify(this.plcSignals))
        },
        workflow: this.workflowConfig.map(function(w, i) {
          return { id: i + 1, name: w.name || '', enabled: !!w.enabled, priority: parseInt(w.priority) || 0 };
        })
      };
    },
    async saveLineConfig() {
      var payload = this.collectLineConfig();
      try {
        var res = await apiPost('/api/config', payload);
        if (res && res.success) {
          this.toast('配置保存成功', 'success');
          await this.loadLineConfig();
        } else {
          this.toast('保存失败: ' + ((res && res.message) || '未知错误'), 'error');
        }
      } catch (e) {
        console.error('保存线体配置失败', e);
        this.toast('保存配置失败: ' + e.message, 'error');
      }
    },
    resetLineConfig() {
      this.racksConfig = [];
      this.machinesConfig = [];
      this.grippersConfig = [];
      this.lineConfig = { rackType: 'fixed', rackCount: 1, rackLayers: 3, rackSlots: 6, gripperType: 'fixed-single', grabMode: 'A-grab-B-place', palletEnabled: false, palletSlots: 4 };
      this.plcConfig = { ip: '', port: 9600 };
      this.toast('已重置，点击保存配置生效', 'info');
    },
    addRack() {
      this.racksConfig.push({
        id: Date.now() + Math.floor(Math.random() * 1000),
        type: 'electrode',
        name: '料架' + (this.racksConfig.length + 1),
        number: this.racksConfig.length + 1,
        rows: 8,
        cols: 10,
        photoSignal: '',
        openRequest: '',
        openSignal: '',
        doorClosed: '',
        doorFlag: '',
        grabStartTaskNo: '',
        placeStartTaskNo: '',
        scanStartTaskNo: ''
      });
    },
    removeRack(index) { this.racksConfig.splice(index, 1); },
    addMachine() {
      this.machinesConfig.push({
        id: Date.now() + Math.floor(Math.random() * 1000),
        type: 'roders',
        number: this.machinesConfig.length + 1,
        ip: '',
        port: '',
        stationCount: 4,
        ncPath: '',
        sessionUrl: '',
        username: '',
        password: ''
      });
    },
    removeMachine(index) { this.machinesConfig.splice(index, 1); },
    addGripper() {
      var gtype = this.lineConfig.gripperType;
      var typeMap = { 'quick-electrode': 'electrode', 'quick-tool': 'tool', 'quick-pallet': 'pallet' };
      var defaultType = typeMap[gtype] || 'steel';
      var typeNames = { steel: '钢件', tool: '刀具', electrode: '电极', pallet: '托盘' };
      var count = this.grippersConfig.filter(function(g) { return g.type === defaultType; }).length + 1;
      this.grippersConfig.push({
        id: Date.now() + Math.floor(Math.random() * 1000),
        type: defaultType,
        name: (typeNames[defaultType] || '快换') + '夹爪' + count,
        number: count,
        slotPhotoSignal: '',
        grabTaskNo: '',
        placeTaskNo: ''
      });
    },
    removeGripper(index) { this.grippersConfig.splice(index, 1); },
    addWorkflowStep() {
      this.workflowConfig.push({ id: this.workflowConfig.length + 1, name: '', enabled: true, priority: 0 });
    },
    removeWorkflowStep(index) { this.workflowConfig.splice(index, 1); },
    importLineConfigExcel() {
      var input = document.getElementById('lineconfig-excel-input');
      if (input) input.click();
    },
    async handleLineConfigExcel(event) {
      var file = event.target.files[0];
      if (!file) return;
      var self = this;
      try {
        var XLSX = window.XLSX || await this.loadXlsxLib();
        if (!XLSX) { this.toast('请先加载xlsx库', 'warning'); return; }
        var reader = new FileReader();
        reader.onload = function(e) {
          try {
            var workbook = XLSX.read(e.target.result, { type: 'array' });
            var sheet = workbook.Sheets[workbook.SheetNames[0]];
            var data = XLSX.utils.sheet_to_json(sheet, { header: 1 });
            self.parseExcelConfig(data);
            self.toast('导入成功', 'success');
          } catch (err) {
            console.error('解析Excel失败:', err);
            self.toast('解析Excel失败', 'error');
          }
        };
        reader.readAsArrayBuffer(file);
      } catch (err) {
        console.error('读取Excel失败:', err);
        this.toast('读取Excel失败', 'error');
      }
      event.target.value = '';
    },
    loadXlsxLib() {
      var self = this;
      return new Promise(function(resolve) {
        if (window.XLSX) { resolve(window.XLSX); return; }
        var script = document.createElement('script');
        script.src = 'https://unpkg.com/xlsx@0.18.5/dist/xlsx.full.min.js';
        script.onload = function() { resolve(window.XLSX); };
        script.onerror = function() { resolve(null); };
        document.head.appendChild(script);
      });
    },
    parseExcelConfig(data) {
      var self = this;
      data.forEach(function(row) {
        var configName = row[0];
        var subItem = row[1];
        var value = row[2];
        if (!configName && !subItem) return;
        if (configName === '线体配置' || subItem) {
          switch (subItem) {
            case '料架类型':
              var rackTypeMap = { '固定料架': 'fixed', '旋转料架': 'rotating' };
              self.lineConfig.rackType = rackTypeMap[value] || 'fixed';
              break;
            case '料架个数':
              if (!isNaN(parseInt(value))) self.lineConfig.rackCount = parseInt(value);
              break;
            case '料架层数':
              if (!isNaN(parseInt(value))) self.lineConfig.rackLayers = parseInt(value);
              break;
            case '料架每层料位数':
              if (!isNaN(parseInt(value))) self.lineConfig.rackSlots = parseInt(value);
              break;
            case '夹爪类型':
              var gripperMap = { '固定单爪': 'fixed-single', '固定双爪': 'fixed-double', '快换爪（电极）': 'quick-electrode', '快换爪（刀具）': 'quick-tool', '快换爪（托盘）': 'quick-pallet' };
              self.lineConfig.gripperType = gripperMap[value] || 'fixed-single';
              break;
            case '抓取方式':
              var grabMap = { 'A抓B放': 'A-grab-B-place', 'B抓A放': 'B-grab-A-place' };
              self.lineConfig.grabMode = grabMap[value] || 'A-grab-B-place';
              break;
            case '随行台有无':
              self.lineConfig.palletEnabled = value === '有';
              break;
            case '随行台料位数':
              if (!isNaN(parseInt(value))) self.lineConfig.palletSlots = parseInt(value);
              break;
          }
        }
      });
    },
    async exportLineConfigExcel() {
      try {
        var XLSX = window.XLSX || await this.loadXlsxLib();
        if (!XLSX) { this.toast('请先加载xlsx库', 'warning'); return; }
        var lc = this.lineConfig;
        var data = [
          ['配置项名称', '配置项细分', '具体数值'],
          ['线体配置', '料架类型', lc.rackType === 'fixed' ? '固定料架' : '旋转料架'],
          ['', '料架个数', lc.rackCount],
          ['', '料架层数', lc.rackLayers],
          ['', '料架每层料位数', lc.rackSlots],
          ['', '夹爪类型', this.gripperTypeName],
          ['', '抓取方式', lc.grabMode === 'A-grab-B-place' ? 'A抓B放' : 'B抓A放'],
          ['', '随行台有无', lc.palletEnabled ? '有' : '无'],
          ['', '随行台料位数', lc.palletSlots],
          ['', '机床信息', this.machinesConfig.map(function(m) { return m.number + '/' + m.ip + '/' + m.type; }).join('; ')],
          ['PLC信号', '机器人', '机器人光电信号'],
          ['', '', '机器人任务开始信号'],
          ['', '', '机器人任务完成信号'],
          ['', '', '机器人报错'],
          ['', '', '机器人安全位置信号'],
          ['', '', '急停触发'],
          ['', '随行台', '各料位光电信号'],
          ['', '预设台', '预设台光电信号'],
          ['', '', '预设台人工操作信号'],
          ['', '清洁站', '清洁站清洁位光电信号']
        ];
        var ws = XLSX.utils.aoa_to_sheet(data);
        var wb = XLSX.utils.book_new();
        XLSX.utils.book_append_sheet(wb, ws, '线体配置');
        XLSX.writeFile(wb, '线体配置导出_' + new Date().toISOString().slice(0, 10) + '.xlsx');
        this.toast('导出成功', 'success');
      } catch (err) {
        console.error('导出Excel失败:', err);
        this.toast('导出失败', 'error');
      }
    },
    startPolling() {
      this.loadDashboard();
      this.syncWorkflowStatus();
      this.loadHomeLog();
      this.pollTimer = setInterval(this.loadDashboard, 3000);
      this.wfTimer = setInterval(this.syncWorkflowStatus, 3000);
      this.logTimer = setInterval(this.loadHomeLog, 5000);
    },
    stopPolling() {
      if (this.pollTimer) clearInterval(this.pollTimer);
      if (this.wfTimer) clearInterval(this.wfTimer);
      if (this.logTimer) clearInterval(this.logTimer);
    }
  },
  mounted() {
    var stored = localStorage.getItem('demo_username');
    this.username = stored || '白晓乾';
    this.startPolling();
    this.loadLogs();
    this.genTaskDemo();
    this.loadShelves();
    this.clockTimer = setInterval(this.updateTime, 1000);
    this.updateTime();
  },
  beforeDestroy() {
    this.stopPolling();
    if (this.clockTimer) clearInterval(this.clockTimer);
  }
});
