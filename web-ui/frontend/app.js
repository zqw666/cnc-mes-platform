new Vue({
  el: '#app',
  data: {
    currentSection: 'home',
    sidebarCollapsed: false,
    datetime: '',
    // Dashboard
    dashboardData: null,
    logs: [],
    homeLog: [],
    // Workflow
    workflowRunning: false,
    workflowPaused: false,
    workflowSteps: [],
    // Rack
    rackData: null,
    knifeRack: [],
    rackAdmin: [],
    rackUpdateCount: 0,
    // Log viewer
    logQuery: { level: "all", keyword: "", page: 1, pageSize: 20 },
    logResults: [],
    logTotal: 0,
    // Line config
    configData: null,
    lineConfig: {
      rackType: "fixed", rackCount: "1", rackLayers: "3", rackSlots: "6",
      gripperType: "fixed-single", grabMode: "A-grab-B-place",
      palletEnabled: "no", palletSlots: ""
    },
    plcConfig: { ip: "192.168.1.1", port: "9600" },
    plcSignals: {
      robot: { photo: "", taskStart: "", taskDone: "", taskNumber: "D100", error: "", safePos: "", estop: "" },
      machine: { startTaskNo: "300" },
      pallet: { slotPhoto: "", grabTaskNo: "", placeTaskNo: "", photoStart: "" },
      preset: { photo: "", manual: "", openRequest: "", openAllow: "", doorLock: "" },
      rotary: { rotate: "", zero: "", grabTaskNo: "", placeTaskNo: "", arrive: "" },
      cleaner: { photo: "", grabTaskNo: "", placeTaskNo: "" }
    },
    plcDm: { task_number: "", robot_task: "", rack_layer_photoelectric: "", gripper_type: "", fixture_position: "", shuttle_part_count: "" },
    racksConfig: [],
    machinesConfig: [],
    grippersConfig: [],
    workflowConfig: [],
    // Temp form fields
    rackForm: { type: "fixed", count: 1, layers: 3, slots: 6, name: "" },
    machineForm: { brand: "roders", ip: "", slots: 4, port: "", ncpath: "" },
    gripperForm: { type: "steel", name: "" },
    // Pallet saved values
    palletSavedValues: {},
    // Timer
    pollTimer: null,
    logTimer: null
  },
  computed: {
    activeSection() { return this.currentSection; },
    displaySteps() {
      return this.workflowSteps.map(function(s) {
        var names = {
          rackoperate: "料架操作", scan: "扫描", preparematerial: "备料",
          loadmachine: "上机", unloadmachine: "下机", unloadmaterial: "下料",
          materialknife: "备刀", changemachine: "换机", changeknife: "换刀"
        };
        return Object.assign({}, s, { displayName: names[s.name] || s.name });
      });
    },
    logPages() {
      var total = this.logTotal || 0;
      return Math.max(1, Math.ceil(total / this.logQuery.pageSize));
    }
  },
  methods: {
    // ==================== Utility ====================
    toast(msg, type) {
      this.$message({ message: msg, type: type || "info", duration: 3000 });
    },

    // ==================== Navigation ====================
    switchSection(section) {
      this.currentSection = section;
      if (section === "lineconfig") this.loadLineConfig();
      if (section === "shelves") this.loadRackStatus();
      if (section === "logs") this.searchLogs();
    },

    // ==================== Dashboard ====================
    async loadDashboard() {
      var res = await ApiService.getDashboard();
      if (res.success) this.dashboardData = res.data;
    },
    clearHomeLog() { this.homeLog = []; },
    appendLog(msg, type) {
      var now = new Date();
      var time = now.getFullYear() + "-" + this.pad(now.getMonth()+1) + "-" + this.pad(now.getDate())
        + " " + this.pad(now.getHours()) + ":" + this.pad(now.getMinutes()) + ":" + this.pad(now.getSeconds());
      this.homeLog.unshift({ time: time, content: msg, type: type || "info" });
      if (this.homeLog.length > 200) this.homeLog.pop();
    },
    pad(n) { return n < 10 ? "0" + n : "" + n; },

    // ==================== Workflow ====================
    async loadWorkflowStatus() {
      var res = await ApiService.getWorkflowStatus();
      if (res.success) {
        this.workflowRunning = res.running;
        this.workflowPaused = res.paused;
        this.workflowSteps = res.steps || [];
      }
    },
    async doWorkflowAction(action) {
      var res;
      if (action === "start") res = await ApiService.startWorkflow("");
      else if (action === "stop") res = await ApiService.stopWorkflow();
      else if (action === "pause") res = await ApiService.pauseWorkflow();
      else if (action === "resume") res = await ApiService.resumeWorkflow();
      if (res && res.success) this.toast(res.message, "success");
      else this.toast(res ? res.message : "操作失败", "error");
      this.loadWorkflowStatus();
    },

    // ==================== Rack ====================
    async loadRackStatus() {
      var res = await ApiService.getRackStatus();
      if (res.success) {
        this.rackData = res.data;
        this.knifeRack = res.data.knifeRack || [];
        this.rackAdmin = res.data.rackAdmin || [];
        this.rackUpdateCount = res.data.rackUpdateCount || 0;
      }
    },
    async updateRack(rackId, pos, status) {
      var res = await ApiService.updateRackStatus({ rackId: rackId, position: pos, status: status });
      if (res.success) this.loadRackStatus();
    },

    // ==================== Logs ====================
    async searchLogs() {
      var params = { level: this.logQuery.level, keyword: this.logQuery.keyword, page: this.logQuery.page, pageSize: this.logQuery.pageSize };
      var res = await ApiService.getLogs(params);
      if (res.success) {
        this.logResults = res.data || [];
        this.logTotal = res.total || 0;
      }
    },
    changeLogPage(page) {
      this.logQuery.page = page;
      this.searchLogs();
    },

    // ==================== Line Config ====================
    async loadLineConfig() {
      var res = await ApiService.getConfig();
      if (!res.success) { this.toast("加载配置失败", "error"); return; }
      var cfg = res.data;
      this.configData = cfg;
      var line = cfg.line || {};
      this.lineConfig = {
        rackType: line.rackType || "fixed",
        rackCount: line.rackCount || "1",
        rackLayers: line.rackLayers || "3",
        rackSlots: line.rackSlots || "6",
        gripperType: line.gripperType || "fixed-single",
        grabMode: line.grabMode || "A-grab-B-place",
        palletEnabled: line.shuttleExists || "no",
        palletSlots: line.shuttleSlots || ""
      };
      var plc = cfg.plc || {};
      this.plcConfig = { ip: plc.ip || "192.168.1.1", port: plc.port || "9600" };
      var signals = plc.signals || {};
      this.plcSignals = {
        robot: Object.assign({ photo: "", taskStart: "", taskDone: "", taskNumber: "D100", error: "", safePos: "", estop: "" }, signals.robot),
        machine: Object.assign({ startTaskNo: "300" }, signals.machine),
        pallet: Object.assign({ slotPhoto: "", grabTaskNo: "", placeTaskNo: "", photoStart: "" }, signals.pallet),
        preset: Object.assign({ photo: "", manual: "", openRequest: "", openAllow: "", doorLock: "" }, signals.preset),
        rotary: Object.assign({ rotate: "", zero: "", grabTaskNo: "", placeTaskNo: "", arrive: "" }, signals.rotary),
        cleaner: Object.assign({ photo: "", grabTaskNo: "", placeTaskNo: "" }, signals.cleaner)
      };
      var dm = plc.dm || {};
      this.plcDm = Object.assign({ task_number: "", robot_task: "", rack_layer_photoelectric: "", gripper_type: "", fixture_position: "", shuttle_part_count: "" }, dm);
      this.racksConfig = (line.racks || []).map(function(r) { return Object.assign({}, r); });
      this.machinesConfig = (line.machines || []).map(function(m) { return Object.assign({}, m); });
      this.grippersConfig = (line.grippers || []).map(function(g) { return Object.assign({}, g); });
      this.workflowConfig = (cfg.workflow || []).map(function(w) { return Object.assign({}, w); });
    },
    async saveLineConfig() {
      var data = this.collectConfig();
      var res = await ApiService.saveConfig(data);
      if (res.success) { this.toast("配置已保存", "success"); this.loadLineConfig(); }
      else this.toast(res.message || "保存失败", "error");
    },
    collectConfig() {
      var cfg = Object.assign({}, this.configData || {});
      if (!cfg.line) cfg.line = {};
      cfg.line.rackType = this.lineConfig.rackType;
      cfg.line.rackCount = this.lineConfig.rackCount;
      cfg.line.rackLayers = this.lineConfig.rackLayers;
      cfg.line.rackSlots = this.lineConfig.rackSlots;
      cfg.line.gripperType = this.lineConfig.gripperType;
      cfg.line.grabMode = this.lineConfig.grabMode;
      cfg.line.shuttleExists = this.lineConfig.palletEnabled;
      cfg.line.shuttleSlots = this.lineConfig.palletSlots;
      cfg.racks = this.racksConfig;
      cfg.machines = this.machinesConfig;
      cfg.grippers = this.grippersConfig;
      if (!cfg.plc) cfg.plc = {};
      cfg.plc.ip = this.plcConfig.ip;
      cfg.plc.port = this.plcConfig.port;
      cfg.plc.signals = this.plcSignals;
      cfg.plc.dm = this.plcDm;
      cfg.workflow = this.workflowConfig;
      return cfg;
    },
    onGripperTypeChange() {
      // Vue reactivity handles visibility via v-if
    },
    addRackFromForm() {
      var f = this.rackForm;
      var count = parseInt(f.count) || 1;
      var layers = parseInt(f.layers) || 8;
      var slots = parseInt(f.slots) || 10;
      for (var i = 0; i < count; i++) {
        this.racksConfig.push({
          id: Date.now() + i,
          type: f.type === "fixed" ? "steel" : "rotating",
          number: this.racksConfig.length + 1,
          rows: layers, cols: slots,
          name: f.name || ("料架" + (this.racksConfig.length + 1))
        });
      }
      this.toast("已添加 " + count + " 个料架，点击「保存配置」提交", "success");
    },
    removeRack(index) { this.racksConfig.splice(index, 1); },
    addMachineFromForm() {
      var f = this.machineForm;
      this.machinesConfig.push({
        id: Date.now(), type: f.brand, number: String(this.machinesConfig.length + 1),
        ip: f.ip, stationCount: String(parseInt(f.slots) || 4), port: f.port, ncPath: f.ncpath,
        sessionUrl: "", username: "", password: ""
      });
      this.toast("已添加机床，点击「保存配置」提交", "success");
    },
    removeMachine(index) { this.machinesConfig.splice(index, 1); },
    addGripper(data) {
      data = data || {};
      var gtype = this.lineConfig.gripperType;
      var typeMap = { "quick-electrode": "electrode", "quick-tool": "tool", "quick-pallet": "pallet" };
      var defaultType = typeMap[gtype] || "steel";
      var typeNames = { steel: "钢件", tool: "刀具", electrode: "电极", pallet: "托盘" };
      var typeName = typeNames[data.type || defaultType] || "快换";
      var count = this.grippersConfig.filter(function(g) { return g.type === (data.type || defaultType); }).length + 1;
      this.grippersConfig.push({
        id: data.id || Date.now(),
        type: data.type || defaultType,
        name: data.name || (typeName + "夹爪" + count),
        number: data.number || count,
        slotPhotoSignal: data.slotPhotoSignal || "",
        grabTaskNo: data.grabTaskNo || "",
        placeTaskNo: data.placeTaskNo || ""
      });
    },
    removeGripper(index) { this.grippersConfig.splice(index, 1); },
    addWorkflowStep() {
      this.workflowConfig.push({ id: Date.now(), name: "", enabled: true, priority: 0 });
    },
    removeWorkflowStep(index) { this.workflowConfig.splice(index, 1); },
    // Pallet toggle
    onPalletChange() {
      var isNo = this.lineConfig.palletEnabled === "no";
      if (isNo) {
        this.palletSavedValues["palletSlots"] = this.lineConfig.palletSlots;
        this.lineConfig.palletSlots = "0";
      } else {
        var saved = this.palletSavedValues["palletSlots"];
        this.lineConfig.palletSlots = (saved && saved !== "0") ? saved : "4";
      }
    },
    resetLineConfig() { this.loadLineConfig(); this.toast("配置已重置", "success"); },
    async importLineConfigExcel() { var input = document.getElementById("lineconfig-excel-input"); if (input) input.click(); },
    handleLineConfigExcel(event) { var file = event.target.files[0]; if (!file) return; this.toast("请手动刷新页面后重新加载", "info"); event.target.value = ""; },

    // Export
    async exportConfigExcel() {
      try {
        var XLSX = window.XLSX;
        if (!XLSX) {
          var script = document.createElement("script");
          script.src = "https://cdn.jsdelivr.net/npm/xlsx@0.18.5/dist/xlsx.full.min.js";
          await new Promise(function(resolve, reject) {
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
          });
          XLSX = window.XLSX;
        }
        var cfg = this.collectConfig();
        var data = [
          ["配置项名称", "配置项细分", "具体数值"],
          ["线体配置", "料架类型", cfg.line.rackType === "fixed" ? "固定料架" : "旋转料架"],
          ["", "料架个数", cfg.line.rackCount],
          ["", "料架层数", cfg.line.rackLayers],
          ["", "料架每层料位数", cfg.line.rackSlots],
          ["", "夹爪类型", this.getGripperName(cfg.line.gripperType)],
          ["", "抓取方式", cfg.line.grabMode === "A-grab-B-place" ? "A抓B放" : "B抓A放"],
          ["", "随行台有无", cfg.line.palletEnabled === "yes" ? "有" : "无"],
          ["", "随行台料位数", cfg.line.palletSlots],
          ["", "机床信息", this.machinesConfig.map(function(m) { return m.number + "/" + m.ip + "/" + m.brand; }).join("; ")],
          ["PLC信号", "料架开门请求", ""],
          ["数据库", "本地数据库", "类型:mysql, 连接:localhost"]
        ];
        var ws = XLSX.utils.aoa_to_sheet(data);
        var wb = XLSX.utils.book_new();
        XLSX.utils.book_append_sheet(wb, ws, "线体配置");
        XLSX.writeFile(wb, "线体配置导出_" + new Date().toISOString().slice(0, 10) + ".xlsx");
        this.toast("导出成功", "success");
      } catch (e) {
        console.error("导出失败:", e);
        this.toast("导出失败", "error");
      }
    },
    getGripperName(type) {
      var map = { "fixed-single": "固定单爪", "fixed-double": "固定双爪", "quick-electrode": "快换爪（电极）", "quick-tool": "快换爪（刀具）", "quick-pallet": "快换爪（托盘）" };
      return map[type] || "固定单爪";
    },

    // ==================== Polling ====================
    startPolling() {
      this.loadDashboard();
      this.loadWorkflowStatus();
      this.pollTimer = setInterval(this.loadDashboard, 5000);
      this.logTimer = setInterval(this.loadWorkflowStatus, 3000);
    },
    stopPolling() {
      if (this.pollTimer) clearInterval(this.pollTimer);
      if (this.logTimer) clearInterval(this.logTimer);
    }
  },
  mounted() {
    this.startPolling();
    // Update datetime every second
    setInterval(function() {
      var now = new Date();
      
      this.datetime = now.getFullYear() + "-" + this.pad(now.getMonth()+1) + "-" + this.pad(now.getDate())
        + " " + this.pad(now.getHours()) + ":" + this.pad(now.getMinutes()) + ":" + this.pad(now.getSeconds());
    }.bind(this), 1000);
  },
  beforeDestroy() {
    this.stopPolling();
  }
});



