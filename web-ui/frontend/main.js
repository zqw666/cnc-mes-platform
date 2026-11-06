
// ==================== 全局共享状态（供各板块函数访问） ====================
var REFRESH_INTERVAL = 3000;
var connected = false;
var machineData = [];
var workflowState = 'idle';
var PAGE_SIZE = 50;
var TASK_DATA = [], currentPage = 1, filteredData = [];
var grids = {};
var shelves = { electrode: [], tool: [], steel: [], aluminum: [] };
var RACKTYPE_MAP = { electrode: 1, tool: 3, steel: 2, aluminum: 5 };
var DEFAULT_SLOTS = { electrode: 80, tool: 20, steel: 12, aluminum: 6 };

// ==================== 更多全局共享状态 ====================
var menuItems = [], sections = [];
var taskTableBody, btnFilter, filterTool, filterMold, btnTop, prevPageBtn, nextPageBtn, pageNumbersWrap;
var tabBtns, tabContents;
var logTableBody, logStartTime, logEndTime, logLevel, logKeyword, logSearchBtn, logResetBtn, logPrevPage, logNextPage, logPageNumbers, logType, btnLogSearch, btnLogReset;
var LOG_PAGE_SIZE = 50, LOG_DATA = [], logCurrentPage = 1, logFilteredData = [];
var lastLogId = 0, LOG_POLL_INTERVAL = 2000;
var configNavItems, configGroups, btnAddMachine, btnSaveConfig, btnImportExcel, excelFileInput, btnExportExcel;
var palletEnabled, palletSlotsField, palletSection, palletSlotsEl, palletInputs, palletSavedValues = {};
var selectAllCheckbox;

document.addEventListener('DOMContentLoaded', () => { restoreLineConfig(); });

document.addEventListener('DOMContentLoaded', async () => {

  // ==================== 全局变量 ====================


  // ==================== Element UI 提示（通过 Vue 注入） ====================

  // ==================== 时钟 ====================
  updateTime();
  setInterval(updateTime, 1000);

  // 用户名
  const stored = localStorage.getItem('demo_username');
  document.getElementById('username').textContent = '用户: ' + (stored || '白晓乾');
  updateNetwork();

  // ==================== 侧边栏 ====================
  const sidebar = document.getElementById('sidebar');
  updateSidebarLeftVar();
  document.getElementById('toggle-btn').addEventListener('click', () => {
    sidebar.classList.toggle('collapsed');
    updateSidebarLeftVar();
  });

  // ==================== 菜单切换 ====================
  menuItems = document.querySelectorAll('.menu-item');
  sections = document.querySelectorAll('.section');
  menuItems.forEach(item => {
    item.addEventListener('click', () => {
      document.querySelector('.menu-item.active')?.classList.remove('active');
      item.classList.add('active');
      const sec = item.dataset.section;
      sections.forEach(s => s.classList.toggle('active', s.id === sec));
    });
  });

  // ==================== 状态文字映射 ====================
  // ==================== 首页：刷新仪表盘 ====================

  taskTableBody = document.querySelector('#task-table tbody');
  selectAllCheckbox = document.getElementById('select-all');
  btnFilter = document.getElementById('btn-filter');
  filterTool = document.getElementById('filter-tool');
  filterMold = document.getElementById('filter-mold');
  btnTop = document.getElementById('btn-top');
  prevPageBtn = document.getElementById('prev-page');
  nextPageBtn = document.getElementById('next-page');
  pageNumbersWrap = document.getElementById('page-numbers');
  palletEnabled = document.getElementById('cfg-pallet-enabled');
  palletSlotsField = document.getElementById('pallet-slots-field');
  palletSection = document.getElementById('pallet-signals-section');
  palletSlotsEl = document.getElementById('cfg-pallet-slots');
  tabBtns = document.querySelectorAll('.tab-btn');
  tabContents = document.querySelectorAll('.tab-content');
  logTableBody = document.querySelector('#log-table tbody');
  logStartTime = document.getElementById('log-start-time');
  logEndTime = document.getElementById('log-end-time');
  logLevel = document.getElementById('log-level');
  logKeyword = document.getElementById('log-keyword');
  logSearchBtn = document.getElementById('btn-log-search');
  logPrevPage = document.getElementById('log-prev-page');
  logNextPage = document.getElementById('log-next-page');
  logPageNumbers = document.getElementById('log-page-numbers');
  logResetBtn = document.getElementById('btn-log-reset');
  logType = document.getElementById('log-type');
  btnLogSearch = document.getElementById('btn-log-search');
  btnLogReset = document.getElementById('btn-log-reset');
  configNavItems = document.querySelectorAll('.config-nav-item');
  configGroups = document.querySelectorAll('.config-group');
  btnAddMachine = document.getElementById('btn-add-machine');
  btnSaveConfig = document.getElementById('btn-save-config');
  btnImportExcel = document.getElementById('btn-import-excel');
  excelFileInput = document.getElementById('excel-file-input');
  btnExportExcel = document.getElementById('btn-export-excel');
  grids = {
    electrode: document.getElementById('electrode-grid'),
    aluminum: document.getElementById('aluminum-grid'),
    tool: document.getElementById('tool-grid'),
    steel: document.getElementById('steel-grid')
  };
  // ==================== 工作流状态 ====================

  // 轮询工作流状态并同步UI

  // ==================== 控制按钮（工作流 API） ====================

  // 开始运行 -> 启动工作流（同时取消所有料架激活状态）
  document.getElementById('btn-start')?.addEventListener('click', () => {
    ['electrode', 'aluminum', 'tool', 'steel'].forEach(t => shelfLock(t));
    doWorkflowAction('start');
  });
  // 暂停运行 -> 暂停工作流
  document.getElementById('btn-pause')?.addEventListener('click', () => {
    if (workflowState === 'paused') {
      // 已暂停时点击恢复
      doWorkflowAction('resume');
    } else {
      doWorkflowAction('pause');
    }
  });
  // 停止运行 -> 停止工作流
  document.getElementById('btn-stop')?.addEventListener('click', () => doWorkflowAction('stop'));
  // 复位 -> 停止工作流 + 重置界面
  document.getElementById('btn-reset')?.addEventListener('click', async () => {
    await doWorkflowAction('stop');
    for (let i = 1; i <= 3; i++) {
      setStatus(document.getElementById('machine' + i + '-status'), '正常');
      const card = document.getElementById('machine' + i + '-card');
      if (card) card.classList.remove('offline');
    }
    setStatus(document.getElementById('robot-status'), '正常');
    document.getElementById('part-name').textContent = '—';
    document.getElementById('process-duration').textContent = '00:00:00';
    document.getElementById('processed-count').textContent = '0';
    document.getElementById('unprocessed-count').textContent = '0';
  });

  // 定时同步工作流状态
  syncWorkflowStatus();
  setInterval(syncWorkflowStatus, REFRESH_INTERVAL);

  // 机床卡片单击/双击事件
  setupMachineCardEvents();

  // 启动数据轮询
  refreshDashboard();
  setInterval(refreshDashboard, REFRESH_INTERVAL);

  // ==================== 任务管理 ====================




  selectAllCheckbox.addEventListener('click', () => {
    const start = (currentPage - 1) * PAGE_SIZE;
    filteredData.slice(start, start + PAGE_SIZE).forEach(d => d.selected = selectAllCheckbox.checked);
    renderTable(currentPage);
  });
  selectAllCheckbox.addEventListener('dblclick', () => {
    const all = filteredData.every(x => x.selected);
    filteredData.forEach(x => x.selected = !all);
    renderTable(currentPage);
  });

  btnFilter.addEventListener('click', applyFilter);
  btnTop?.addEventListener('click', () => {
    const sel = TASK_DATA.filter(x => x.selected);
    TASK_DATA = sel.concat(TASK_DATA.filter(x => !x.selected));
    applyFilter();
  });
  prevPageBtn.addEventListener('click', () => { if (currentPage > 1) renderTable(currentPage - 1); });
  nextPageBtn.addEventListener('click', () => {
    const pages = Math.max(1, Math.ceil(filteredData.length / PAGE_SIZE));
    if (currentPage < pages) renderTable(currentPage + 1);
  });

  genDemo();
  renderTable(1);

  // ==================== 料架管理 ====================
  // racktype: 1=electrode(80槽) 2=steel(12槽) 3=tool 5=aluminum(铝板电极)

  // 从后端加载料架数据

  // 料架状态 -> 图标颜色

  // 状态文字映射





  tabBtns.forEach(btn => {
    btn.addEventListener('click', () => {
      tabBtns.forEach(b => b.classList.remove('active'));
      tabContents.forEach(c => c.classList.remove('active'));
      btn.classList.add('active');
      document.getElementById(btn.dataset.tab + '-tab').classList.add('active');
      renderShelves(btn.dataset.tab);
    });
  });

  ['electrode', 'aluminum', 'tool', 'steel'].forEach(type => {
    const el = id => document.getElementById(type + '-' + id);
    if (el('select-all')) el('select-all').addEventListener('click', () => shelfSelectAll(type));
    if (el('activate')) el('activate').addEventListener('click', () => shelfActivate(type));
    if (el('lock')) el('lock').addEventListener('click', () => shelfLock(type));
    if (el('disable')) el('disable').addEventListener('click', () => shelfSetStatus(type, 'disable'));
    if (el('no-material')) el('no-material').addEventListener('click', () => shelfSetStatus(type, 'no-material'));
    if (el('urgent')) el('urgent').addEventListener('click', () => shelfSetStatus(type, 'urgent'));
  });

  try { await loadShelves(); } catch (e) { console.warn('料架加载失败', e); }
  renderShelves('electrode');
  renderShelves('aluminum');
  renderShelves('tool');
  renderShelves('steel');

  // ==================== 料架数据轮询 ====================
  // 子程序更新料位后调用 /api/rack/notify，主控程序递增版本号，前端据此刷新料架图标
  let lastRackVersion = 0;
  async function pollRackUpdate() {
    try {
      const res = await apiGet('/api/rack/status');
      if (res && res.success && typeof res.version === 'number' && res.version !== lastRackVersion) {
        lastRackVersion = res.version;
        await loadShelves();
        renderShelves('electrode');
        renderShelves('aluminum');
        renderShelves('tool');
        renderShelves('steel');
      }
    } catch (e) { /* silent */ }
  }
  setInterval(pollRackUpdate, 2000);

  // ==================== 日志查询 ====================






  btnLogSearch.addEventListener('click', applyLogFilter);
  btnLogReset.addEventListener('click', resetLogFilter);
  logPrevPage.addEventListener('click', () => { if (logCurrentPage > 1) renderLogTable(logCurrentPage - 1); });
  logNextPage.addEventListener('click', () => {
    const pages = Math.max(1, Math.ceil(logFilteredData.length / LOG_PAGE_SIZE));
    if (logCurrentPage < pages) renderLogTable(logCurrentPage + 1);
  });

  genLogDemo();
  renderLogTable(1);

  // ==================== 实时日志轮询 ====================

  // 轮询获取最新日志

  // 从API响应添加日志到主界面

  // 从API响应添加日志到日志查询页面

  // WebSocket 相关函数已移除，改用轮询方式获取实时日志

  // addRealtimeLog 函数已由 addRealtimeLogFromApi 替代

  // addHomeLog 函数已由 addHomeLogFromApi 替代

  // HTML转义函数

  // 清空主界面日志

  // 绑定清空按钮事件
  document.getElementById('btn-clear-home-log')?.addEventListener('click', clearHomeLog);


  // 启动日志轮询
  pollRecentLogs(); // 立即执行一次
  setInterval(pollRecentLogs, LOG_POLL_INTERVAL);

  // ==================== 线体配置页面 ====================
  // 注意：此处使用局部变量，全局的 machinesConfig 在文件下方定义

  // 左侧导航切换
  configNavItems.forEach(item => {
    item.addEventListener('click', () => {
      configNavItems.forEach(i => i.classList.remove('active'));
      configGroups.forEach(g => g.classList.remove('active'));
      item.classList.add('active');
      document.getElementById('config-group-' + item.dataset.group).classList.add('active');
    });
  });

  // 从后端加载配置

  // 渲染机床列表

  // 添加机床
  if (btnAddMachine) btnAddMachine.addEventListener('click', () => {
    const nextId = machinesConfig.length > 0
      ? String(Math.max(...machinesConfig.map(m => parseInt(m.id) || 0)) + 1)
      : '1';
    machinesConfig.push({ id: nextId, number: nextId, type: '', ip: '', port: '', stationCount: 4, ncPath: '' });
    renderMachines();
  });

  // 收集当前表单配置数据

  // 保存配置
  if (btnSaveConfig) btnSaveConfig.addEventListener('click', async () => {
    try {
      const data = collectConfig();
      const res = await apiPost('/api/config', data);
      if (res.success) {
        toast('配置保存成功', 'success');
      } else {
        toast('保存失败: ' + (res.message || '未知错误'), 'error');
      }
    } catch (e) {
      toast('保存失败: ' + e.message, 'error');
    }
  });

  // Excel导入
  if (btnImportExcel && excelFileInput) {
    btnImportExcel.addEventListener('click', () => excelFileInput.click());
    excelFileInput.addEventListener('change', async (e) => {
      const file = e.target.files[0];
      if (!file) return;
      try {
        const XLSX = window.XLSX || (await loadXlsxLib());
        const data = await file.arrayBuffer();
        const wb = XLSX.read(data, { type: 'array' });
        const sheetName = wb.SheetNames[0];
        const ws = wb.Sheets[sheetName];
        const rows = XLSX.utils.sheet_to_json(ws, { header: 1, defval: '' });

      // 解析Excel行 -> 填充表单
      let currentCategory = '';
      let currentSub = '';
      const excelData = {};
      rows.forEach((row, idx) => {
        if (idx === 0) return; // 跳过表头
        const cat = String(row[0] || '').trim();
        const sub = String(row[1] || '').trim();
        const val = String(row[2] || '').trim();
        if (cat) currentCategory = cat;
        if (sub) currentSub = sub;
        const key = currentCategory + '|' + currentSub;
        if (!excelData[key]) excelData[key] = val;
      });

      // 映射到表单
      const mapping = {
        '线体配置|夹爪类型': 'cfg-gripper-type',
        '线体配置|抓取方式': 'cfg-grab-mode',
        '线体配置|随行台有无': 'cfg-pallet-enabled',
        '线体配置|随行台料位数': 'cfg-shuttle-slots'
      };
      Object.entries(mapping).forEach(([excelKey, elId]) => {
        if (excelData[excelKey] !== undefined) {
          const el = document.getElementById(elId);
          if (el) {
            const val = excelData[excelKey];
            // select元素需检查option是否存在
            if (el.tagName === 'SELECT') {
              const opts = Array.from(el.options).map(o => o.value);
              if (opts.includes(val)) el.value = val;
            } else {
              el.value = val;
            }
          }
        }
      });

      // 随行台联动
      if (typeof updatePalletFields === 'function') updatePalletFields();

      // PLC信号映射
      const plcMapping = {
        'PLC信号|料架开门请求': 'cfg-door-open-request',
        'PLC信号|允许开门信号': 'cfg-door-open-allow',
        'PLC信号|料架门关信号': 'cfg-door-closed',
        'PLC信号|各层光电变化信号': 'cfg-photoelectric-change',
        'PLC信号|料架忙碌信号': 'cfg-rack-busy',
        'PLC信号|机器人光电信号': 'cfg-robot-photoelectric',
        'PLC信号|机器人任务开始信号': 'cfg-task-start-signal',
        'PLC信号|机器人任务完成信号': 'cfg-task-complete-signal',
        'PLC信号|机器人报错': 'cfg-robot-error',
        'PLC信号|机器人安全位置信号': 'cfg-robot-safe',
        'PLC信号|急停触发': 'cfg-emergency-stop',
        'PLC信号|钢件快换夹爪料位光电信号': 'cfg-steel-gripper-photoelectric',
        'PLC信号|电极快换夹爪料位光电信号': 'cfg-electrode-gripper-photoelectric',
        'PLC信号|各料位光电信号': 'cfg-shuttle-slot-photoelectric',
        'PLC信号|预设台光电信号': 'cfg-preset-photoelectric',
        'PLC信号|预设台人工操作信号': 'cfg-preset-manual-signal',
        'PLC信号|清洁站清洁位光电信号': 'cfg-clean-photoelectric',
        'PLC信号|清洁站门开到位信号': 'cfg-clean-door-open',
        'PLC信号|清洁站门关到位信号': 'cfg-clean-door-closed'
      };
      Object.entries(plcMapping).forEach(([excelKey, elId]) => {
        if (excelData[excelKey] !== undefined) {
          const el = document.getElementById(elId);
          if (el) el.value = excelData[excelKey];
        }
      });

      // 数据库映射
      const dbMapping = {
        '数据库|本地数据库配置': null // 特殊处理
      };
      // 简单匹配：如果excel中数据库配置行包含连接信息
      Object.entries(excelData).forEach(([key, val]) => {
        if (key.startsWith('数据库|') && val) {
          // 尝试解析 "数据库类型，连接语句" 格式
          // 这里只做提示，具体值需用户在表单中填
        }
      });

      toast('Excel导入成功，请检查并补充未自动映射的配置项', 'success');
    } catch (e) {
      toast('Excel导入失败: ' + e.message, 'error');
    }
    // 清空file input以便重复选择同一文件
    excelFileInput.value = '';
  });
  }

  // 加载XLSX库（动态）

  // 导出Excel
  if (btnExportExcel) btnExportExcel.addEventListener('click', async () => {
    try {
      const XLSX = window.XLSX || (await loadXlsxLib());
      const rows = [
        ['配置项名称', '配置项细分', '具体数值'],
        ['线体配置', '夹爪类型', document.getElementById('cfg-gripper-type').value],
        ['', '抓取方式', document.getElementById('cfg-grab-mode').value],
        ['', '随行台有无', document.getElementById('cfg-shuttle-exists').value],
        ['', '随行台料位数', document.getElementById('cfg-shuttle-slots').value],
        ['PLC信号', '料架开门请求', document.getElementById('cfg-door-open-request').value],
        ['', '允许开门信号', document.getElementById('cfg-door-open-allow').value],
        ['', '料架门关信号', document.getElementById('cfg-door-closed').value],
        ['', '各层光电变化信号', document.getElementById('cfg-photoelectric-change').value],
        ['', '料架忙碌信号', document.getElementById('cfg-rack-busy').value],
        ['', '机器人光电信号', document.getElementById('cfg-robot-photoelectric').value],
        ['', '机器人任务开始信号', document.getElementById('cfg-task-start-signal').value],
        ['', '机器人任务完成信号', document.getElementById('cfg-task-complete-signal').value],
        ['', '机器人报错', document.getElementById('cfg-robot-error').value],
        ['', '机器人安全位置信号', document.getElementById('cfg-robot-safe').value],
        ['', '急停触发', document.getElementById('cfg-emergency-stop').value],
        ['', '钢件快换夹爪料位光电信号', document.getElementById('cfg-steel-gripper-photoelectric').value],
        ['', '电极快换夹爪料位光电信号', document.getElementById('cfg-electrode-gripper-photoelectric').value],
        ['', '各料位光电信号', document.getElementById('cfg-shuttle-slot-photoelectric').value],
        ['', '预设台光电信号', document.getElementById('cfg-preset-photoelectric').value],
        ['', '预设台人工操作信号', document.getElementById('cfg-preset-manual-signal').value],
        ['', '清洁站清洁位光电信号', document.getElementById('cfg-clean-photoelectric').value],
        ['', '清洁站门开到位信号', document.getElementById('cfg-clean-door-open').value],
        ['', '清洁站门关到位信号', document.getElementById('cfg-clean-door-closed').value],
        ['数据库', '本地数据库配置', document.getElementById('cfg-db-type').value],
        ['', 'eman数据库配置', document.getElementById('cfg-eman-db-type').value]
      ];
      const ws = XLSX.utils.aoa_to_sheet(rows);
      const wb = XLSX.utils.book_new();
      XLSX.utils.book_append_sheet(wb, ws, '线体配置项');
      XLSX.writeFile(wb, '线体配置项.xlsx');
      toast('导出成功', 'success');
    } catch (e) {
      toast('导出失败: ' + e.message, 'error');
    }
  });

  // 加载配置数据（页面加载时立即获取，不依赖DOMContentLoaded）
  loadConfig();

  // ==================== Element UI 消息挂载 ====================
  if (window.ELEMENT && typeof ELEMENT.Message === 'function') {
    if (typeof ELEMENT.use === 'function') ELEMENT.use(ELEMENT.Message);
    $message = (opts) => {
      if (typeof opts === 'string') opts = { message: opts };
      ELEMENT.Message(opts);
    };
  }

});

// 收集配置数据

// 保存配置

// 加载配置

// 重置配置

// 导入Excel

// 处理Excel导入

// 动态加载xlsx库

// 解析Excel配置

// 导出Excel


// 菜单切换时加载配置
document.addEventListener('DOMContentLoaded', () => {
  document.querySelectorAll('.menu-item').forEach(item => {
    item.addEventListener('click', () => {
      const section = item.getAttribute('data-section');
      if (section === 'lineconfig') {
        setTimeout(() => loadLineConfig(), 100);
      }
    });
  });
  
  palletInputs = [
    document.getElementById('cfg-pallet-slots'),
    document.getElementById('cfg-signal-pallet-slot-photo'),
    document.getElementById('cfg-signal-pallet-grab-task'),
    document.getElementById('cfg-signal-pallet-place-task'),
    document.getElementById('cfg-signal-pallet-photo-start')
  ];
  palletSavedValues = {};
  // 随行台有无 -> 禁用/启用随行台相关配置


  if (palletEnabled) {
    palletEnabled.addEventListener('change', updatePalletFields);
    // 初始化时也执行一次
    updatePalletFields();
  }
  
  // 初始化：先恢复本地缓存，再从后端加载配置（后端为准，保证 config.json 的机床可见）
  restoreLineConfig();
  loadLineConfig();
});

