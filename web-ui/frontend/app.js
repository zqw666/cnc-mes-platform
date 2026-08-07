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
    shelfDefaultSlots: { electrode: 80, tool: 20, steel: 12, pallet: 16 }
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
    isWorkflowIdle() { return this.workflowState === 'idle'; }
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
          var racktype = self.shelfRacktypeMap[t.key];
          var rows = allRows.filter(function(r) { return Number(r.racktype) === racktype; }).sort(function(a, b) { return a.id - b.id; });
          var map = {};
          rows.forEach(function(r) { map[Number(r.id)] = r; });
          var arr = [];
          for (var i = 1; i <= self.shelfDefaultSlots[t.key]; i++) {
            var row = map[i];
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
