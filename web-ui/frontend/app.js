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
    clockTimer: null
  },
  computed: {
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
    switchSection(s) { this.currentSection = s; },
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
    this.clockTimer = setInterval(this.updateTime, 1000);
    this.updateTime();
  },
  beforeDestroy() {
    this.stopPolling();
    if (this.clockTimer) clearInterval(this.clockTimer);
  }
});
