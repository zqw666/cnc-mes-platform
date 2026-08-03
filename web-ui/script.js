// ==================== 全局函数（必须在 DOMContentLoaded 外，HTML onclick 可访问） ====================
function toast(msg, type = 'info') {
  let el = document.getElementById('toast-box');
  if (!el) {
    el = document.createElement('div');
    el.id = 'toast-box';
    el.style.cssText = 'position:fixed;top:20px;right:20px;z-index:9999;display:flex;flex-direction:column;gap:6px';
    document.body.appendChild(el);
  }
  const bg = { success: '#4caf50', error: '#f44336', info: '#2196f3', warning: '#ff9800' }[type] || '#2196f3';
  const box = document.createElement('div');
  box.textContent = msg;
  box.style.cssText = `background:${bg};color:#fff;padding:10px 16px;border-radius:6px;box-shadow:0 2px 8px rgba(0,0,0,.2);font-size:13px;max-width:260px`;
  el.appendChild(box);
  setTimeout(() => box.remove(), 3000);
}

// 从线体配置栏表单添加料架（仅本地，点击保存配置后统一提交）
function addRackFromForm() {
  const rackType = document.getElementById('cfg-rack-type').value;
  const rackCount = parseInt(document.getElementById('cfg-rack-count').value) || 1;
  const rackLayers = parseInt(document.getElementById('cfg-rack-layers').value) || 8;
  const rackSlots = parseInt(document.getElementById('cfg-rack-slots').value) || 10;
  const rackName = document.getElementById('cfg-rack-name').value.trim() || '';
  const typeMap = { 'fixed': '1', 'rotating': '2' };
  const type = typeMap[rackType] || '1';
  for (let i = 0; i < rackCount; i++) {
    addRack({
      id: Date.now() + i,
      type: type,
      number: rackCount > 1 ? racksConfig.length + 1 : i + 1,
      rows: rackLayers,
      cols: rackSlots,
      name: rackName || `料架${racksConfig.length + 1}`
    });
  }
  toast(`已添加 ${rackCount} 个料架，点击「保存配置」提交`, 'success');
}

// 从线体配置栏表单添加机床（仅本地，点击保存配置后统一提交）
function addMachineFromForm() {
  const brand = document.getElementById('cfg-machine-brand').value || 'roders';
  const ip = document.getElementById('cfg-machine-ip').value || '';
  const slots = parseInt(document.getElementById('cfg-machine-slots').value) || 4;
  const port = parseInt(document.getElementById('cfg-machine-port').value) || '';
  const ncpath = document.getElementById('cfg-machine-ncpath').value || '';
  addMachine({
    id: Date.now(),
    brand: brand,
    number: machinesConfig.length + 1,
    ip: ip,
    slots: slots,
    port: port,
    ncpath: ncpath
  });
  toast('已添加机床，点击「保存配置」提交', 'success');
}

// 夹爪类型切换：固定双爪显示抓取方式，快换类型显示夹爪列表
function onGripperTypeChange() {
  var type = document.getElementById('cfg-gripper-type').value;
  var grabModeEl = document.getElementById('grab-mode-field');
  var gripperSection = document.getElementById('gripper-section');
  // 固定双爪显示抓取方式
  if (grabModeEl) grabModeEl.style.display = (type === 'fixed-double') ? '' : 'none';
  // 快换类型显示夹爪列表
  if (gripperSection) gripperSection.style.display = (type.startsWith('quick-')) ? 'block' : 'none';
  if (type.startsWith('quick-')) renderGrippers();
}

// 添加夹爪到本地配置
function addGripper(data) {
  data = data || {};
  var typeMap = { 'quick-electrode': 'electrode', 'quick-tool': 'tool', 'quick-pallet': 'pallet' };
  var gtype = document.getElementById('cfg-gripper-type').value;
  var defaultType = typeMap[gtype] || 'steel';
  var typeNames = { 'steel': '钢件', 'tool': '刀具', 'electrode': '电极', 'pallet': '托盘' };
  var typeName = typeNames[data.type || defaultType] || '快换';
  var count = grippersConfig.filter(function(g) { return g.type === (data.type || defaultType); }).length + 1;
  var gripper = {
    id: data.id || Date.now(),
    type: data.type || defaultType,
    name: data.name || (typeName + '夹爪' + count),
    number: data.number || count,
    slotPhotoSignal: data.slotPhotoSignal || '',
    grabTaskNo: data.grabTaskNo || '',
    placeTaskNo: data.placeTaskNo || ''
  };
  grippersConfig.push(gripper);
  persistLineConfig();
  renderGrippers();
}

function removeGripper(index) {
  grippersConfig.splice(index, 1);
  persistLineConfig();
  renderGrippers();
}

function updateGripper(index, field, value) {
  if (grippersConfig[index]) {
    grippersConfig[index][field] = value;
    persistLineConfig();
  }
}

function renderGrippers() {
  var container = document.getElementById('gripper-list');
  if (!container) return;
  if (grippersConfig.length === 0) { container.innerHTML = ''; return; }
  container.innerHTML = grippersConfig.map(function(g, index) {
    return '<div class="card-item">' +
      '<div class="card-header"><span class="card-title">' + (g.name || '夹爪' + (index+1)) + '</span>' +
      '<button class="btn-remove" onclick="removeGripper(' + index + ')" title="删除">×</button></div>' +
      '<div class="card-body">' +
      '<div class="config-field"><label>名称</label><input type="text" class="config-input" value="' + (g.name || '') + '" placeholder="夹爪名称" onchange="updateGripper(' + index + ', \'name\', this.value)"></div>' +
      '<div class="config-field"><label>类型</label><select class="config-input" onchange="updateGripper(' + index + ', \'type\', this.value)">' +
        '<option value="steel"' + (g.type==='steel'?' selected':'') + '>钢件夹爪</option>' +
        '<option value="tool"' + (g.type==='tool'?' selected':'') + '>刀具夹爪</option>' +
        '<option value="electrode"' + (g.type==='electrode'?' selected':'') + '>电极夹爪</option>' +
        '<option value="pallet"' + (g.type==='pallet'?' selected':'') + '>托盘夹爪</option>' +
      '</select></div>' +
      '<div class="config-field"><label>料位光电信号</label><input type="text" class="config-input" value="' + (g.slotPhotoSignal || '') + '" placeholder="如：104.00" onchange="updateGripper(' + index + ', \'slotPhotoSignal\', this.value)"></div>' +
      '<div class="config-field"><label>取夹爪任务号</label><input type="text" class="config-input" value="' + (g.grabTaskNo || '') + '" placeholder="如：D400" onchange="updateGripper(' + index + ', \'grabTaskNo\', this.value)"></div>' +
      '<div class="config-field"><label>放夹爪任务号</label><input type="text" class="config-input" value="' + (g.placeTaskNo || '') + '" placeholder="如：D500" onchange="updateGripper(' + index + ', \'placeTaskNo\', this.value)"></div>' +
      '</div></div>';
  }).join('');
}

// 添加料架到本地配置
function addRack(data = {}) {
  const rack = {
    id: data.id || Date.now(),
    type: data.type || 'electrode',   // electrode | tool | steel | pallet
    name: data.name || `料架${racksConfig.length + 1}`,
    number: data.number || racksConfig.length + 1,
    rows: data.rows || 8,
    cols: data.cols || 10,
    photoSignal: data.photoSignal || '',
    openRequest: data.openRequest || '',   // 人工开门请求信号
    openSignal: data.openSignal || '',      // 允许开门信号（软件→PLC）
    doorClosed: data.doorClosed || '',      // 门状态信号（1=关, 0=开）
    doorFlag: data.doorFlag || '',          // 门状态标志信号
    grabStartTaskNo: data.grabStartTaskNo || '',  // 抓取开始任务号
    placeStartTaskNo: data.placeStartTaskNo || '', // 放置开始任务号
    scanStartTaskNo: data.scanStartTaskNo || ''    // 扫描开始任务号
  };
  racksConfig.push(rack);
  persistLineConfig();
  renderRacks();
}

// 添加机床到本地配置
function addMachine(data = {}) {
  const machine = {
    id: data.id || Date.now(),
    type: data.type || 'roders',
    number: data.number || machinesConfig.length + 1,
    ip: data.ip || '',
    port: data.port || '',
    stationCount: data.stationCount || 4,
    ncpath: data.ncpath || ''
  };
  machinesConfig.push(machine);
  persistLineConfig();
  renderMachines();
}

// ==================== 线体配置全局数据（必须在 DOMContentLoaded 外） ====================
var racksConfig = [];
var machinesConfig = [];
var workflowConfig = [];
var grippersConfig = [];

function restoreLineConfig() {
  try {
    var saved = localStorage.getItem('line_racks_config');
    if (saved) racksConfig = JSON.parse(saved);
    var savedMac = localStorage.getItem('line_machines_config');
    if (savedMac) machinesConfig = JSON.parse(savedMac);
    var savedWf = localStorage.getItem('line_workflow_config');
    if (savedWf) workflowConfig = JSON.parse(savedWf);
    var savedGrip = localStorage.getItem('line_grippers_config');
    if (savedGrip) grippersConfig = JSON.parse(savedGrip);
  } catch(e) { racksConfig = []; machinesConfig = []; workflowConfig = []; grippersConfig = []; }
  renderRacks();
  renderMachines();
  renderWorkflow();
  renderGrippers();
}

function persistLineConfig() {
  try {
    localStorage.setItem('line_racks_config', JSON.stringify(racksConfig));
    localStorage.setItem('line_machines_config', JSON.stringify(machinesConfig));
    localStorage.setItem('line_workflow_config', JSON.stringify(workflowConfig));
    localStorage.setItem('line_grippers_config', JSON.stringify(grippersConfig));
  } catch(e) {}
}

function renderRacks() {
  var container = document.getElementById('rack-list');
  if (!container) return;
  var section = container.closest('.config-subsection');
  if (section) section.style.display = racksConfig.length === 0 ? 'none' : 'block';
  if (racksConfig.length === 0) { container.innerHTML = ''; return; }
  container.innerHTML = racksConfig.map(function(rack, index) {
    var rtype = rack.type || 'electrode';
    return '<div class="card-item" data-rack-id="' + rack.id + '">' +
      '<div class="card-header"><span class="card-title">' + (rack.name || '料架 ' + rack.number) + '</span>' +
      '<button class="btn-remove" onclick="removeRack(' + index + ')" title="删除">×</button></div>' +
      '<div class="card-body">' +
      '<div class="config-field"><label>料架名称</label><input type="text" class="config-input" value="' + (rack.name || '') + '" placeholder="料架名称" onchange="updateRack(' + index + ', \'name\', this.value)"></div>' +
      '<div class="config-field"><label>料架类型</label><select class="config-input" onchange="updateRack(' + index + ', \'type\', this.value)">' +
        '<option value="electrode"' + (rtype==='electrode'?' selected':'') + '>电极料架</option>' +
        '<option value="tool"' + (rtype==='tool'?' selected':'') + '>刀具料架</option>' +
        '<option value="steel"' + (rtype==='steel'?' selected':'') + '>钢件料架</option>' +
        '<option value="pallet"' + (rtype==='pallet'?' selected':'') + '>托盘料架</option>' +
      '</select></div>' +
      '<div class="config-field"><label>料架编号</label><input type="number" class="config-input" value="' + (rack.number || 1) + '" min="1" onchange="updateRack(' + index + ', \'number\', parseInt(this.value))"></div>' +
      '<div class="config-field"><label>行数</label><input type="number" class="config-input" value="' + (rack.rows || 8) + '" min="1" onchange="updateRack(' + index + ', \'rows\', parseInt(this.value))"></div>' +
      '<div class="config-field"><label>列数</label><input type="number" class="config-input" value="' + (rack.cols || 10) + '" min="1" onchange="updateRack(' + index + ', \'cols\', parseInt(this.value))"></div>' +
      '<div class="config-field"><label>光电信号</label><input type="text" class="config-input" value="' + (rack.photoSignal || '') + '" placeholder="DM区地址" onchange="updateRack(' + index + ', \'photoSignal\', this.value)"></div>' +
      '<div class="config-field"><label>开门请求信号</label><input type="text" class="config-input" value="' + (rack.openRequest || '') + '" placeholder="如：103.02" onchange="updateRack(' + index + ', \'openRequest\', this.value)"></div>' +
      '<div class="config-field"><label>允许开门信号</label><input type="text" class="config-input" value="' + (rack.openSignal || '') + '" placeholder="软件输出给PLC" onchange="updateRack(' + index + ', \'openSignal\', this.value)"></div>' +
      '<div class="config-field"><label>门状态信号</label><input type="text" class="config-input" value="' + (rack.doorClosed || '') + '" placeholder="如：103.01" onchange="updateRack(' + index + ', \'doorClosed\', this.value)"></div>' +
      '<div class="config-field"><label>门状态标志信号</label><input type="text" class="config-input" value="' + (rack.doorFlag || '') + '" placeholder="脉冲信号地址" onchange="updateRack(' + index + ', \'doorFlag\', this.value)"></div>' +
      '<div class="config-field"><label>抓取开始任务号</label><input type="text" class="config-input" value="' + (rack.grabStartTaskNo || '') + '" placeholder="如: D100" onchange="updateRack(' + index + ', \'grabStartTaskNo\', this.value)"></div>' +
      '<div class="config-field"><label>放置开始任务号</label><input type="text" class="config-input" value="' + (rack.placeStartTaskNo || '') + '" placeholder="如: D200" onchange="updateRack(' + index + ', \'placeStartTaskNo\', this.value)"></div>' +
      '<div class="config-field"><label>扫描开始任务号</label><input type="text" class="config-input" value="' + (rack.scanStartTaskNo || '') + '" placeholder="如: D300" onchange="updateRack(' + index + ', \'scanStartTaskNo\', this.value)"></div>' +
      '</div></div>';
  }).join('');
}

function renderMachines() {
  var container = document.getElementById('machine-list');
  if (!container) return;
  var section = container.closest('.config-subsection');
  if (section) section.style.display = machinesConfig.length === 0 ? 'none' : 'block';
  if (machinesConfig.length === 0) { container.innerHTML = ''; return; }
  container.innerHTML = machinesConfig.map(function(m, index) {
    var isSiemens = (m.type || '').toLowerCase() === 'siemense' || (m.type || '').toLowerCase() === 'siemens';
    var siemensFields = '';
    if (isSiemens) {
      siemensFields =
        '<div class="config-field siemens-field"><label>会话URL</label><input type="text" class="config-input" value="' + (m.sessionUrl || '') + '" placeholder="如: opc.tcp://192.168.1.1:4840" onchange="updateMachine(' + index + ', \'sessionUrl\', this.value)"></div>' +
        '<div class="config-field siemens-field"><label>登录用户名</label><input type="text" class="config-input" value="' + (m.username || '') + '" placeholder="OPC UA 用户名" onchange="updateMachine(' + index + ', \'username\', this.value)"></div>' +
        '<div class="config-field siemens-field"><label>登录密码</label><input type="password" class="config-input" value="' + (m.password || '') + '" placeholder="OPC UA 密码" onchange="updateMachine(' + index + ', \'password\', this.value)"></div>';
    }
    return '<div class="card-item" data-machine-id="' + m.id + '">' +
      '<div class="card-header"><span class="card-title">' + (m.name || m.type + '-' + m.number) + '</span>' +
      '<button class="btn-remove" onclick="removeMachine(' + index + ')" title="删除">×</button></div>' +
      '<div class="card-body">' +
      '<div class="config-field"><label>机床编号</label><input type="text" class="config-input" value="' + (m.number || '') + '" placeholder="如: 1" onchange="updateMachine(' + index + ', \'number\', this.value)"></div>' +
      '<div class="config-field"><label>品牌</label><input type="text" class="config-input mc-type" value="' + (m.type || '') + '" placeholder="如: fanuc" onchange="updateMachine(' + index + ', \'type\', this.value);renderMachines()"></div>' +
      '<div class="config-field"><label>IP</label><input type="text" class="config-input mc-ip" value="' + (m.ip || '') + '" placeholder="IP" onchange="updateMachine(' + index + ', \'ip\', this.value)"></div>' +
      '<div class="config-field"><label>端口</label><input type="text" class="config-input" value="' + (m.port || '') + '" placeholder="如: 8193" onchange="updateMachine(' + index + ', \'port\', this.value)"></div>' +
      '<div class="config-field"><label>工位数</label><input type="number" class="config-input" value="' + (m.stationCount || 4) + '" min="1" onchange="updateMachine(' + index + ', \'stationCount\', parseInt(this.value))"></div>' +
      '<div class="config-field"><label>NC路径</label><input type="text" class="config-input mc-nc" value="' + (m.ncPath || '') + '" placeholder="NC路径" onchange="updateMachine(' + index + ', \'ncPath\', this.value)"></div>' +
      siemensFields +
      '</div></div>';
  }).join('');
}

function updateRack(index, field, value) {
  if (racksConfig[index]) {
    racksConfig[index][field] = value;
    persistLineConfig();
  }
}

function removeRack(index) {
  racksConfig.splice(index, 1);
  persistLineConfig();
  renderRacks();
}

function updateMachine(index, field, value) {
  if (machinesConfig[index]) {
    machinesConfig[index][field] = value;
    persistLineConfig();
  }
}

function removeMachine(index) {
  machinesConfig.splice(index, 1);
  persistLineConfig();
  renderMachines();
}

function renderWorkflow() {
  var container = document.getElementById('workflow-list');
  if (!container) return;
  var section = container.closest('.config-subsection');
  if (section) section.style.display = workflowConfig.length === 0 ? 'none' : 'block';
  if (workflowConfig.length === 0) { container.innerHTML = ''; return; }
  container.innerHTML = workflowConfig.map(function(step, index) {
    return '<tr data-wf-index="' + index + '">' +
      '<td class="wf-seq">' + (index + 1) + '</td>' +
      '<td class="wf-name"><input type="text" class="config-input" value="' + (step.name || '') + '" placeholder="如：上料" onchange="updateWorkflowField(' + index + ', \'name\', this.value)"></td>' +
      '<td class="wf-enabled"><input type="checkbox" class="wf-checkbox" ' + (step.enabled ? 'checked' : '') + ' onchange="updateWorkflowField(' + index + ', \'enabled\', this.checked)"></td>' +
      '<td class="wf-priority"><input type="number" class="config-input" value="' + (step.priority != null ? step.priority : 0) + '" min="0" onchange="updateWorkflowField(' + index + ', \'priority\', parseInt(this.value) || 0)"></td>' +
      '<td class="wf-op"><button class="btn-remove" onclick="removeWorkflowStep(' + index + ')" title="删除">×</button></td>' +
      '</tr>';
  }).join('');
}

function addWorkflowStep() {
  workflowConfig.push({
    id: workflowConfig.length + 1,
    name: '',
    enabled: true,
    priority: 0
  });
  persistLineConfig();
  renderWorkflow();
}

function removeWorkflowStep(index) {
  workflowConfig.splice(index, 1);
  persistLineConfig();
  renderWorkflow();
}

function updateWorkflowField(index, field, value) {
  if (workflowConfig[index]) {
    workflowConfig[index][field] = value;
    workflowConfig[index].id = index + 1;
    persistLineConfig();
  }
}

// 初始化：从 localStorage 恢复（等待 DOM 就绪）
// ==================== 全局常量 ====================
const API_BASE = 'http://localhost:8888';

document.addEventListener('DOMContentLoaded', () => { restoreLineConfig(); });

document.addEventListener('DOMContentLoaded', async () => {

  // ==================== 全局变量 ====================
  const REFRESH_INTERVAL = 3000;

  let connected = false;
  let machineData = [];

  // ==================== HTTP 工具 ====================
  async function apiGet(path) {
    const r = await fetch(API_BASE + path);
    if (!r.ok) throw new Error('HTTP ' + r.status);
    return r.json();
  }
  async function apiPost(path, body = {}) {
    const r = await fetch(API_BASE + path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });
    if (!r.ok) throw new Error('HTTP ' + r.status);
    return r.json();
  }

  // ==================== Element UI 提示（通过 Vue 注入） ====================
  let $message = null;
  function toast(msg, type) {
    if ($message) { $message({ message: msg, type: type || 'info', duration: 2500 }); return; }
    // fallback alert
    console.log('[' + type + ']', msg);
  }

  // ==================== 时钟 ====================
  function pad(n) { return n < 10 ? '0' + n : n; }
  function updateTime() {
    const now = new Date();
    document.getElementById('datetime').textContent =
      now.getFullYear() + '-' + pad(now.getMonth() + 1) + '-' + pad(now.getDate()) +
      ' ' + pad(now.getHours()) + ':' + pad(now.getMinutes()) + ':' + pad(now.getSeconds());
  }
  updateTime();
  setInterval(updateTime, 1000);

  // 用户名
  const stored = localStorage.getItem('demo_username');
  document.getElementById('username').textContent = '用户: ' + (stored || '白晓乾');

  // ==================== 网络状态 ====================
  function updateNetwork() {
    const online = navigator.onLine && connected;
    const el = document.getElementById('net-indicator');
    el.textContent = online ? '在线' : '离线';
    el.className = online ? 'online' : 'offline';
  }

  // ==================== 侧边栏 ====================
  const sidebar = document.getElementById('sidebar');
  function updateSidebarLeftVar() {
    const collapsed = sidebar.classList.contains('collapsed');
    const val = collapsed
      ? getComputedStyle(document.documentElement).getPropertyValue('--sidebar-collapsed')
      : getComputedStyle(document.documentElement).getPropertyValue('--sidebar-width');
    document.documentElement.style.setProperty('--sidebar-left', val.trim());
  }
  updateSidebarLeftVar();
  document.getElementById('toggle-btn').addEventListener('click', () => {
    sidebar.classList.toggle('collapsed');
    updateSidebarLeftVar();
  });

  // ==================== 菜单切换 ====================
  const menuItems = document.querySelectorAll('.menu-item');
  const sections = document.querySelectorAll('.section');
  menuItems.forEach(item => {
    item.addEventListener('click', () => {
      document.querySelector('.menu-item.active')?.classList.remove('active');
      item.classList.add('active');
      const sec = item.dataset.section;
      sections.forEach(s => s.classList.toggle('active', s.id === sec));
    });
  });

  // ==================== 状态文字映射 ====================
  function setStatus(el, state) {
    el.classList.remove('normal', 'running', 'paused', 'stopped', 'auto');
    const map = { '正常': 'normal', '运行': 'running', '暂停': 'paused', '停止': 'stopped', '自动': 'auto' };
    if (map[state]) el.classList.add(map[state]);
    el.textContent = state;
  }

  // ==================== 首页：刷新仪表盘 ====================
  async function refreshDashboard() {
    try {
      const res = await apiGet('/api/dashboard');
      if (!res.success) return;
      const d = res.data;

      machineData = d.machines || [];
      const m = machineData[0] || {};

      // 加工零件名称
      document.getElementById('part-name').textContent = m.current_workpiece || '—';

      // 加工时长（秒 -> HH:MM:SS）
      const sec = m.processing_duration || 0;
      document.getElementById('process-duration').textContent =
        pad(Math.floor(sec / 3600)) + ':' + pad(Math.floor((sec % 3600) / 60)) + ':' + pad(sec % 60);

      // 今日统计
      document.getElementById('processed-count').textContent = d.today_processed || 0;
      document.getElementById('unprocessed-count').textContent = d.today_unprocessed || 0;

      // 机床状态 -> 页面状态文字
      let text = '正常';
      if (m.status === 1) {
        if (m.workstate === 1) text = '运行';
        else if (m.workstate === 2 || m.error_message) text = '停止';
        else text = '正常';
      } else {
        text = '正常';
      }
      setStatus(document.getElementById('machine-status'), text);
      setStatus(document.getElementById('shelf-status'), text);
      setStatus(document.getElementById('robot-status'), text);

      if (!connected) {
        connected = true;
        updateNetwork();
      }
    } catch (e) {
      connected = false;
      updateNetwork();
    }
  }

  // ==================== 工作流状态 ====================
  let workflowState = 'idle'; // 'idle' | 'running' | 'paused'

  // 轮询工作流状态并同步UI
  async function syncWorkflowStatus() {
    try {
      const res = await apiGet('/api/workflow/status');
      if (!res.success) return;
      const prev = workflowState;
      workflowState = res.running ? (res.paused ? 'paused' : 'running') : 'idle';

      // 状态变化时更新设备显示
      if (workflowState !== prev) {
        if (workflowState === 'running') {
          setStatus(document.getElementById('machine-status'), '运行');
          setStatus(document.getElementById('robot-status'), '运行');
          setStatus(document.getElementById('shelf-status'), '运行');
        } else if (workflowState === 'paused') {
          setStatus(document.getElementById('machine-status'), '暂停');
          setStatus(document.getElementById('robot-status'), '暂停');
          setStatus(document.getElementById('shelf-status'), '暂停');
        } else {
          setStatus(document.getElementById('machine-status'), '正常');
          setStatus(document.getElementById('robot-status'), '正常');
          setStatus(document.getElementById('shelf-status'), '自动');
        }
      }
    } catch (e) {
      // 网络异常，保持上次状态
    }
  }

  // ==================== 控制按钮（工作流 API） ====================
  async function doWorkflowAction(action) {
    const labels = { start: '启动', pause: '暂停', stop: '停止', resume: '恢复' };
    try {
      const res = await apiPost('/api/workflow/' + action);
      if (res.success) {
        toast(labels[action] + '命令已发送', 'success');
        // 立即同步状态
        await syncWorkflowStatus();
      } else {
        toast(labels[action] + '失败: ' + (res.message || '未知错误'), 'warning');
      }
    } catch (e) {
      toast(labels[action] + '失败: ' + e.message, 'error');
    }
  }

  // 开始运行 -> 启动工作流
  document.getElementById('btn-start')?.addEventListener('click', () => doWorkflowAction('start'));
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
    setStatus(document.getElementById('machine-status'), '正常');
    setStatus(document.getElementById('robot-status'), '正常');
    setStatus(document.getElementById('shelf-status'), '自动');
    document.getElementById('part-name').textContent = '—';
    document.getElementById('process-duration').textContent = '00:00:00';
    document.getElementById('processed-count').textContent = '0';
    document.getElementById('unprocessed-count').textContent = '0';
  });

  // 定时同步工作流状态
  syncWorkflowStatus();
  setInterval(syncWorkflowStatus, REFRESH_INTERVAL);

  // 启动数据轮询
  refreshDashboard();
  setInterval(refreshDashboard, REFRESH_INTERVAL);

  // ==================== 任务管理 ====================
  const taskTableBody = document.querySelector('#task-table tbody');
  const selectAllCheckbox = document.getElementById('select-all');
  const btnFilter = document.getElementById('btn-filter');
  const filterTool = document.getElementById('filter-tool');
  const filterMold = document.getElementById('filter-mold');
  const btnTop = document.getElementById('btn-top');
  const prevPageBtn = document.getElementById('prev-page');
  const nextPageBtn = document.getElementById('next-page');
  const pageNumbersWrap = document.getElementById('page-numbers');
  const PAGE_SIZE = 50;
  let TASK_DATA = [], currentPage = 1, filteredData = [];

  function genDemo() {
    TASK_DATA = [];
    for (let i = 1; i <= 137; i++) {
      TASK_DATA.push({
        id: i,
        monId: 'MON-' + (1000 + i),
        partName: '零件-' + i,
        toolNo: i % 10 === 0 ? 'T-100' : 'T-' + (i % 20),
        mold: 'M-' + (i % 7 + 1),
        machine: '机台-' + (i % 5 + 1),
        selected: false
      });
    }
    filteredData = TASK_DATA.slice();
  }

  function renderTable(page) {
    currentPage = page || 1;
    const start = (currentPage - 1) * PAGE_SIZE;
    taskTableBody.innerHTML = '';
    filteredData.slice(start, start + PAGE_SIZE).forEach((row, idx) => {
      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td class="col-select"><input class="row-select" data-id="${row.id}" type="checkbox" ${row.selected ? 'checked' : ''}></td>
        <td class="col-no">${start + idx + 1}</td>
        <td>${row.monId}</td>
        <td>${row.partName}</td>
        <td>${row.toolNo}</td>
        <td>${row.mold}</td>
        <td>${row.machine}</td>
      `;
      taskTableBody.appendChild(tr);
    });
    renderPager();
    taskTableBody.querySelectorAll('.row-select').forEach(cb => {
      cb.addEventListener('change', () => {
        const item = TASK_DATA.find(x => x.id === Number(cb.dataset.id));
        if (item) item.selected = cb.checked;
      });
    });
  }

  function renderPager() {
    const pages = Math.max(1, Math.ceil(filteredData.length / PAGE_SIZE));
    pageNumbersWrap.innerHTML = '';
    for (let p = 1; p <= pages; p++) {
      const b = document.createElement('button');
      b.textContent = p;
      if (p === currentPage) b.classList.add('active');
      b.addEventListener('click', () => renderTable(p));
      pageNumbersWrap.appendChild(b);
    }
    prevPageBtn.disabled = currentPage <= 1;
    nextPageBtn.disabled = currentPage >= pages;
  }

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

  function applyFilter() {
    const t = filterTool.value.trim();
    const m = filterMold.value.trim();
    filteredData = TASK_DATA.filter(item => {
      if (t && !String(item.toolNo).includes(t)) return false;
      if (m && !String(item.mold).includes(m)) return false;
      return true;
    });
    renderTable(1);
  }
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
  const tabBtns = document.querySelectorAll('.tab-btn');
  const tabContents = document.querySelectorAll('.tab-content');
  const grids = {
    electrode: document.getElementById('electrode-grid'),
    tool: document.getElementById('tool-grid'),
    steel: document.getElementById('steel-grid'),
    pallet: document.getElementById('pallet-grid')
  };
  const shelves = { electrode: [], tool: [], steel: [], pallet: [] };
  // racktype: 1=electrode(80槽) 2=steel(12槽) 3=tool 4=pallet
  const RACKTYPE_MAP = { electrode: 1, tool: 3, steel: 2, pallet: 4 };
  const DEFAULT_SLOTS = { electrode: 80, tool: 20, steel: 12, pallet: 16 };

  // 从后端加载料架数据
  async function loadShelves() {
    const fillDefault = () => {
      Object.keys(grids).forEach(type => {
        shelves[type] = [];
        for (let i = 1; i <= DEFAULT_SLOTS[type]; i++) {
          shelves[type].push({ id: i, rfid: '', posstatus: 0, workstate: 0, racknum: 1, activated: false, selected: false });
        }
      });
    };
    try {
      const res = await apiGet('/api/rack/status');
      if (!res.success) { fillDefault(); return; }
      const allRows = Array.isArray(res.data?.rack_administrate) ? res.data.rack_administrate : [];
      Object.keys(grids).forEach(type => {
        const racktype = RACKTYPE_MAP[type];
        const rows = allRows.filter(r => Number(r.racktype) === racktype).sort((a, b) => a.id - b.id);
        const map = {};
        rows.forEach(r => { map[Number(r.id)] = r; });
        shelves[type] = [];
        for (let i = 1; i <= DEFAULT_SLOTS[type]; i++) {
          const row = map[i];
          shelves[type].push(row ? {
            id: Number(row.id), rfid: row.rfid || '', posstatus: Number(row.posstatus || 0),
            workstate: Number(row.workstate || 0), scanstatus: Number(row.scanstatus || 0),
            priority: Number(row.priority || 0), racknum: Number(row.racknum || 1),
            activated: false, selected: false
          } : { id: i, rfid: '', posstatus: 0, workstate: 0, scanstatus: 0, priority: 0, racknum: 1, activated: false, selected: false });
        }
      });
    } catch (e) {
      console.warn('料架加载失败，使用默认空槽位', e);
      fillDefault();
    }
  }

  // 料架状态 -> 图标颜色
  function shelfStatus(row, type) {
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
  }

  // 状态文字映射
  function getStatusText(status) {
    const map = {
      'processing': '加工中',
      'pending': '待加工',
      'completed': '已完成',
      'to-scan': '待扫描',
      'no-material': '无料',
      'error': '错误',
      'disabled': '禁用',
      'urgent': '急件'
    };
    return map[status] || status;
  }

  function renderShelves(type) {
    const grid = grids[type];
    if (!grid) { console.error('Grid element not found for type:', type); return; }
    grid.innerHTML = '';
    shelves[type].forEach(shelf => {
      const status = shelfStatus(shelf, type);
      const item = document.createElement('div');
      item.className = 'shelf-item ' + (shelf.activated ? 'activated' : 'locked');
      item.dataset.status = status;
      const partName = shelf.partName || (shelf.rfid ? '未知工件' : '-');
      item.innerHTML = `
        <input type="checkbox" class="shelf-checkbox" data-id="${shelf.id}" ${shelf.selected ? 'checked' : ''}>
        <div class="shelf-position">#${shelf.id}</div>
        <div class="shelf-info-row"><span class="shelf-info-label">监控:</span><span class="shelf-info-value shelf-rfid">${shelf.rfid || '-'}</span></div>
        <div class="shelf-info-row"><span class="shelf-info-label">工件:</span><span class="shelf-info-value shelf-partname">${partName}</span></div>
        <div class="shelf-status status-text-${status}"></div>
      `;
      grid.appendChild(item);
    });
    grid.querySelectorAll('.shelf-checkbox').forEach(cb => {
      cb.addEventListener('change', () => {
        const s = shelves[type].find(x => x.id === Number(cb.dataset.id));
        if (s) s.selected = cb.checked;
      });
    });
  }

  function showLoading(type, show) {
    const grid = grids[type];
    if (!grid) return;
    let loader = grid.querySelector('.loading-overlay');
    if (show) {
      if (!loader) { loader = document.createElement('div'); loader.className = 'loading-overlay'; loader.innerHTML = '<div class="spinner"></div>'; grid.appendChild(loader); }
      loader.style.display = 'flex';
    } else if (loader) { loader.style.display = 'none'; }
  }

  async function shelfSetStatus(type, status) {
    const selected = shelves[type].filter(s => s.selected);
    if (selected.length === 0) { toast('请先选择要操作的料位', 'warning'); return; }
    const ids = selected.map(s => s.id);
    showLoading(type, true);
    try {
      const res = await apiPost('/api/rack/status', { type, ids, status });
      if (res.success) {
        selected.forEach(s => {
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
        toast('状态已更新', 'success');
      } else { toast(res.message || '更新失败', 'error'); }
      renderShelves(type);
    } catch (e) { toast('更新失败: ' + e.message, 'error'); }
    finally { showLoading(type, false); }
  }

  function shelfActivate(type) { shelves[type].forEach(s => s.activated = true); renderShelves(type); }
  function shelfLock(type) { shelves[type].forEach(s => s.activated = false); renderShelves(type); }
  function shelfSelectAll(type) { shelves[type].filter(s => s.activated).forEach(s => s.selected = true); renderShelves(type); }

  tabBtns.forEach(btn => {
    btn.addEventListener('click', () => {
      tabBtns.forEach(b => b.classList.remove('active'));
      tabContents.forEach(c => c.classList.remove('active'));
      btn.classList.add('active');
      document.getElementById(btn.dataset.tab + '-tab').classList.add('active');
      renderShelves(btn.dataset.tab);
    });
  });

  ['electrode', 'tool', 'steel', 'pallet'].forEach(type => {
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
  renderShelves('tool');
  renderShelves('steel');
  renderShelves('pallet');

  // ==================== 日志查询 ====================
  const logTableBody = document.querySelector('#log-table tbody');
  const logStartTime = document.getElementById('log-start-time');
  const logEndTime = document.getElementById('log-end-time');
  const logType = document.getElementById('log-type');
  const logKeyword = document.getElementById('log-keyword');
  const btnLogSearch = document.getElementById('btn-log-search');
  const btnLogReset = document.getElementById('btn-log-reset');
  const logPrevPage = document.getElementById('log-prev-page');
  const logNextPage = document.getElementById('log-next-page');
  const logPageNumbers = document.getElementById('log-page-numbers');
  const LOG_PAGE_SIZE = 50;
  let LOG_DATA = [], logCurrentPage = 1, logFilteredData = [];

  function genLogDemo() {
    const types = ['INFO', 'ROBOT', 'ERROR'];
    const msgs = {
      INFO: ['系统启动完成', '用户登录成功', '配置文件加载', '定时任务执行', '数据库连接正常'],
      ROBOT: ['机器人开始执行任务', '抓取工件完成', '放置工件完成', '运动轨迹规划', '等待加工完成信号'],
      ERROR: ['通信超时', '工件识别失败', '运动超限', '传感器异常', '加工超时']
    };
    LOG_DATA = [];
    const now = Date.now();
    for (let i = 1; i <= 200; i++) {
      const type = types[Math.floor(Math.random() * 3)];
      LOG_DATA.push({
        id: i,
        time: new Date(now - Math.floor(Math.random() * 7 * 86400000)),
        type,
        message: msgs[type][Math.floor(Math.random() * msgs[type].length)] + ' #' + i
      });
    }
    logFilteredData = LOG_DATA.slice();
  }

  function renderLogTable(page) {
    logCurrentPage = page || 1;
    const start = (logCurrentPage - 1) * LOG_PAGE_SIZE;
    logTableBody.innerHTML = '';
    logFilteredData.slice(start, start + LOG_PAGE_SIZE).forEach((row, idx) => {
      const tr = document.createElement('tr');
      const typeMap = { INFO: ['系统日志', 'type-system'], ROBOT: ['机器人日志', 'type-robot'], ERROR: ['错误日志', 'type-error'] };
      const [tText, tClass] = typeMap[row.type] || [row.type, ''];
      tr.innerHTML = `
        <td class="col-no">${start + idx + 1}</td>
        <td>${row.time.toLocaleString('zh-CN')}</td>
        <td><span class="log-type-badge ${tClass}">${tText}</span></td>
        <td class="log-message">${row.message}</td>
      `;
      logTableBody.appendChild(tr);
    });
    renderLogPager();
  }

  function renderLogPager() {
    const pages = Math.max(1, Math.ceil(logFilteredData.length / LOG_PAGE_SIZE));
    logPageNumbers.innerHTML = '';
    for (let p = 1; p <= pages; p++) {
      const b = document.createElement('button');
      b.textContent = p;
      if (p === logCurrentPage) b.classList.add('active');
      b.addEventListener('click', () => renderLogTable(p));
      logPageNumbers.appendChild(b);
    }
    logPrevPage.disabled = logCurrentPage <= 1;
    logNextPage.disabled = logCurrentPage >= pages;
  }

  function applyLogFilter() {
    const sv = logStartTime.value ? new Date(logStartTime.value).getTime() : null;
    const ev = logEndTime.value ? new Date(logEndTime.value).getTime() : null;
    const tv = logType.value;
    const kv = logKeyword.value.trim().toLowerCase();
    logFilteredData = LOG_DATA.filter(item => {
      if (sv && item.time.getTime() < sv) return false;
      if (ev && item.time.getTime() > ev) return false;
      if (tv && item.type !== tv) return false;
      if (kv && !item.message.toLowerCase().includes(kv)) return false;
      return true;
    });
    renderLogTable(1);
  }

  function resetLogFilter() {
    logStartTime.value = '';
    logEndTime.value = '';
    logType.value = '';
    logKeyword.value = '';
    logFilteredData = LOG_DATA.slice();
    renderLogTable(1);
  }

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
  let lastLogId = 0; // 记录上次获取的最大日志ID
  const LOG_POLL_INTERVAL = 2000; // 每2秒轮询一次

  // 轮询获取最新日志
  async function pollRecentLogs() {
    try {
      // 获取最新的50条日志
      const res = await apiGet('/api/logs?page=1&pageSize=50');
      if (!res.success || !res.data.logs) return;
      
      const logs = res.data.logs;
      if (logs.length === 0) return;
      
      // 找出比 lastLogId 更新的日志
      const newLogs = logs.filter(log => log.id > lastLogId);
      if (newLogs.length === 0) return;
      
      // 更新 lastLogId
      lastLogId = Math.max(...logs.map(l => l.id));
      
      // 按时间正序显示（旧的先显示）
      newLogs.reverse().forEach(log => {
        // 显示在主界面实时日志框
        addHomeLogFromApi(log);
        // 显示在日志查询页面
        addRealtimeLogFromApi(log);
      });
      
    } catch (e) {
      console.warn('轮询日志失败:', e);
    }
  }

  // 从API响应添加日志到主界面
  function addHomeLogFromApi(log) {
    const homeLogBody = document.getElementById('home-log-tbody');
    if (!homeLogBody) return;

    const tr = document.createElement('tr');
    tr.className = 'new-log';

    // 格式化时间（只显示时分秒）
    const time = log.time ? new Date(log.time).toLocaleTimeString('zh-CN', { hour12: false }) : '--:--:--';
    
    // 日志类型样式映射
    const typeMap = {
      'INFO': ['系统', 'info'],
      'SUCCESS': ['成功', 'success'],
      'WARN': ['警告', 'warn'],
      'ERROR': ['错误', 'error'],
      'ROBOT': ['机器人', 'robot']
    };
    const [typeText, typeClass] = typeMap[log.level] || [log.level, 'info'];

    // 日志内容：显示分包显示名 + 内容
    let content = log.content || '';
    if (log.stepDisplay) {
      content = `[${log.stepDisplay}] ${content}`;
    } else if (log.step) {
      content = `[${log.step}] ${content}`;
    }

    tr.innerHTML = `
      <td class="log-col-time">${time}</td>
      <td class="log-col-content">${escapeHtml(content)}</td>
      <td class="log-col-type"><span class="log-type-tag ${typeClass}">${typeText}</span></td>
    `;

    // 插入到顶部
    if (homeLogBody.firstChild) {
      homeLogBody.insertBefore(tr, homeLogBody.firstChild);
    } else {
      homeLogBody.appendChild(tr);
    }

    // 限制显示条数（最多50条）
    while (homeLogBody.children.length > 50) {
      homeLogBody.removeChild(homeLogBody.lastChild);
    }

    // 3秒后移除高亮
    setTimeout(() => {
      tr.className = '';
    }, 3000);
  }

  // 从API响应添加日志到日志查询页面
  function addRealtimeLogFromApi(log) {
    const logTableBody = document.querySelector('#log-table tbody');
    if (!logTableBody) return;

    const tr = document.createElement('tr');
    tr.style.background = '#fffbe6'; // 高亮新日志
    tr.style.animation = 'fadeIn 0.5s';

    const typeMap = {
      'INFO': ['系统日志', 'type-system'],
      'SUCCESS': ['成功日志', 'type-success'],
      'WARN': ['警告日志', 'type-warning'],
      'ERROR': ['错误日志', 'type-error'],
      'ROBOT': ['机器人日志', 'type-robot']
    };
    const [tText, tClass] = typeMap[log.level] || [log.level, ''];

    const time = log.time ? new Date(log.time).toLocaleString('zh-CN') : '--';
    let content = log.content || '';
    if (log.stepDisplay) {
      content = `[${log.stepDisplay}] ${content}`;
    } else if (log.step) {
      content = `[${log.step}] ${content}`;
    }
    
    tr.innerHTML = `
      <td class="col-no">●</td>
      <td>${time}</td>
      <td><span class="log-type-badge ${tClass}">${tText}</span></td>
      <td class="log-message">${escapeHtml(content)}</td>
    `;

    // 插入到表格顶部
    if (logTableBody.firstChild) {
      logTableBody.insertBefore(tr, logTableBody.firstChild);
    } else {
      logTableBody.appendChild(tr);
    }

    // 限制行数，最多保留 100 行
    while (logTableBody.children.length > 100) {
      logTableBody.removeChild(logTableBody.lastChild);
    }

    // 3秒后移除高亮
    setTimeout(() => {
      tr.style.background = '';
    }, 3000);
  }

  // WebSocket 相关函数已移除，改用轮询方式获取实时日志

  // addRealtimeLog 函数已由 addRealtimeLogFromApi 替代

  // addHomeLog 函数已由 addHomeLogFromApi 替代

  // HTML转义函数
  function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }

  // 清空主界面日志
  function clearHomeLog() {
    const homeLogBody = document.getElementById('home-log-tbody');
    if (homeLogBody) {
      homeLogBody.innerHTML = '';
    }
  }

  // 绑定清空按钮事件
  document.getElementById('btn-clear-home-log')?.addEventListener('click', clearHomeLog);

  function updateShelfFromStatus(shelf, status, data) {
    // 根据状态更新料位属性
    switch (status) {
      case 'no-material':
        shelf.posstatus = 0;
        shelf.workstate = 0;
        break;
      case 'to-scan':
        shelf.posstatus = 1;
        shelf.scanstatus = 0;
        shelf.workstate = 0;
        break;
      case 'pending':
        shelf.posstatus = 1;
        shelf.scanstatus = 1;
        shelf.workstate = 0;
        shelf.priority = 0;
        break;
      case 'urgent':
        shelf.posstatus = 1;
        shelf.scanstatus = 1;
        shelf.workstate = 0;
        shelf.priority = 1;
        break;
      case 'processing':
        shelf.posstatus = 1;
        shelf.workstate = 1;
        break;
      case 'completed':
        shelf.posstatus = 1;
        shelf.workstate = 2;
        break;
      case 'scan-failed':
        shelf.posstatus = 1;
        shelf.scanstatus = 2;
        shelf.workstate = 0;
        break;
    }
    if (data.rfid !== undefined) {
      shelf.rfid = data.rfid;
    }
  }

  // 启动日志轮询
  pollRecentLogs(); // 立即执行一次
  setInterval(pollRecentLogs, LOG_POLL_INTERVAL);

  // ==================== 线体配置页面 ====================
  const configNavItems = document.querySelectorAll('.config-nav-item');
  const configGroups = document.querySelectorAll('.config-group');
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
  async function loadConfig() {
    try {
      const res = await apiGet('/api/config');
      if (!res.success) return;
      const d = res.data;

      // 线体配置
      const line = d.line || {};
      document.getElementById('cfg-rack-type').value = line.rackType || '固定料架';
      document.getElementById('cfg-rack-count').value = line.rackCount || '';
      document.getElementById('cfg-rack-layers').value = line.rackLayers || '';
      document.getElementById('cfg-rack-slots').value = line.rackSlots || '';
      document.getElementById('cfg-gripper-type').value = line.gripperType || '固定单爪';
      document.getElementById('cfg-grab-mode').value = line.grabMode || 'A抓B放';
      document.getElementById('cfg-pallet-enabled').value = line.palletEnabled ? 'yes' : 'no';
      document.getElementById('cfg-pallet-slots').value = line.palletSlots || '4';
      updatePalletFields();

      // 机床信息
      machinesConfig = Array.isArray(line.machines) && line.machines.length > 0 ? line.machines : (machinesConfig.length > 0 ? machinesConfig : []);
      renderMachines();
      if (machinesConfig.length === 0) { console.warn('[loadConfig] API返回machines为空，使用当前machinesConfig'); }

      // 料架配置
      racksConfig = Array.isArray(line.racks) && line.racks.length > 0 ? line.racks : (racksConfig.length > 0 ? racksConfig : []);
      renderRacks();
      if (racksConfig.length === 0) { console.warn('[loadConfig] API返回racks为空，使用当前racksConfig'); }

      // PLC信号（嵌套结构：signals.rack, signals.robot, signals.pallet, signals.preset, signals.cleaner）
      const plc = d.plc || {};
      const signals = plc.signals || {};
      const rackSignals = signals.rack || {};
      const robotSignals = signals.robot || {};
      const palletSignals = signals.pallet || {};
      const presetSignals = signals.preset || {};
      const cleanerSignals = signals.cleaner || {};
      document.getElementById('cfg-plc-ip').value = plc.ip || '';
      document.getElementById('cfg-plc-port').value = plc.port || '';
      // rack 信号
      document.getElementById('cfg-door-open-request').value = rackSignals.openRequest || '';
      document.getElementById('cfg-door-open-allow').value = rackSignals.openAllow || '';
      document.getElementById('cfg-door-closed').value = rackSignals.doorClosed || '';
      document.getElementById('cfg-photoelectric-change').value = rackSignals.photoChange || '';
      document.getElementById('cfg-rack-busy').value = rackSignals.busy || '';
      // robot 信号
      document.getElementById('cfg-robot-photoelectric').value = robotSignals.photo || '';
      document.getElementById('cfg-task-start-signal').value = robotSignals.taskStart || '';
      document.getElementById('cfg-task-complete-signal').value = robotSignals.taskDone || '';
      document.getElementById('cfg-robot-error').value = robotSignals.error || '';
      document.getElementById('cfg-robot-safe').value = robotSignals.safePos || '';
      document.getElementById('cfg-emergency-stop').value = robotSignals.estop || '';
      // pallet 信号
      var pslot  = document.getElementById('cfg-signal-pallet-slot-photo');
      var pgrab  = document.getElementById('cfg-signal-pallet-grab-task');
      var pplace = document.getElementById('cfg-signal-pallet-place-task');
      var pstart = document.getElementById('cfg-signal-pallet-photo-start');
      if (pslot)  pslot.value  = palletSignals.slotPhoto || '';
      if (pgrab)  pgrab.value  = palletSignals.grabTaskNo || '';
      if (pplace) pplace.value = palletSignals.placeTaskNo || '';
      if (pstart) pstart.value = palletSignals.photoStart || '';
      // preset 信号
      document.getElementById('cfg-preset-photoelectric').value = presetSignals.photo || '';
      document.getElementById('cfg-preset-manual-signal').value = presetSignals.manual || '';
      // cleaner 信号
      document.getElementById('cfg-clean-photoelectric').value = cleanerSignals.photo || '';
      document.getElementById('cfg-clean-door-open').value = cleanerSignals.doorOpen || '';
      document.getElementById('cfg-clean-door-closed').value = cleanerSignals.doorClosed || '';

      // PLC DM区
      const dm = plc.dm || {};
      document.getElementById('cfg-dm-task-number').value = dm.task_number || '';
      document.getElementById('cfg-dm-robot-task').value = dm.robot_task || '';
      document.getElementById('cfg-dm-rack-photoelectric').value = dm.rack_layer_photoelectric || '';
      document.getElementById('cfg-dm-gripper-type').value = dm.gripper_type || '';
      document.getElementById('cfg-dm-fixture-position').value = dm.fixture_position || '';
      document.getElementById('cfg-dm-shuttle-part-count').value = dm.shuttle_part_count || '';

      // 数据库
      const db = d.database || {};
      document.getElementById('cfg-db-type').value = db.type || 'mysql';
      document.getElementById('cfg-db-host').value = db.host || '';
      document.getElementById('cfg-db-port').value = db.port || '';
      document.getElementById('cfg-db-user').value = db.user || '';
      document.getElementById('cfg-db-password').value = db.password || '';
      document.getElementById('cfg-db-name').value = db.database || '';
      document.getElementById('cfg-db-charset').value = db.charset || '';

      // eman数据库
      const eman = d.emanDatabase || {};
      document.getElementById('cfg-eman-db-type').value = eman.type || 'mysql';
      document.getElementById('cfg-eman-db-host').value = eman.host || '';
      document.getElementById('cfg-eman-db-port').value = eman.port || '';
      document.getElementById('cfg-eman-db-user').value = eman.user || '';
      document.getElementById('cfg-eman-db-password').value = eman.password || '';
      document.getElementById('cfg-eman-db-name').value = eman.database || '';
      document.getElementById('cfg-eman-db-charset').value = eman.charset || '';

    } catch (e) {
      console.warn('配置加载失败', e);
    }
  }

  // 渲染机床列表
function renderMachines() {
    const container = document.getElementById('machine-list');
    container.innerHTML = '';
    machinesConfig.forEach((m, i) => {
      const card = document.createElement('div');
      card.className = 'machine-card';
      card.innerHTML = `
        <div class="config-field"><label>机床编号</label><input class="config-input mc-number" value="${escapeHtml(m.number || '')}" placeholder="如: 1" /></div>
        <div class="config-field"><label>机床品牌</label><input class="config-input mc-type" value="${escapeHtml(m.type || '')}" placeholder="如: fanuc" /></div>
        <div class="config-field"><label>机床IP</label><input class="config-input mc-ip" value="${escapeHtml(m.ip || '')}" placeholder="如: 192.168.1.101" /></div>
        <div class="config-field"><label>通讯端口</label><input class="config-input mc-port" value="${escapeHtml(m.port || '')}" placeholder="如: 8193" /></div>
        <div class="config-field"><label>工位数</label><input class="config-input mc-station" type="number" value="${m.stationCount || 4}" min="1" /></div>
        <div class="config-field"><label>NC路径</label><input class="config-input mc-nc" value="${escapeHtml(m.ncPath || '')}" placeholder="如: C:\\NC\\Mac1" /></div>
        <button class="btn-remove-machine" title="删除机床">✕</button>
      `;
      card.querySelector('.mc-number').addEventListener('input', e => { machinesConfig[i].number = e.target.value; persistLineConfig(); });
      card.querySelector('.mc-type').addEventListener('input', e => { machinesConfig[i].type = e.target.value; persistLineConfig(); });
      card.querySelector('.mc-ip').addEventListener('input', e => { machinesConfig[i].ip = e.target.value; persistLineConfig(); });
      card.querySelector('.mc-port').addEventListener('input', e => { machinesConfig[i].port = e.target.value; persistLineConfig(); });
      card.querySelector('.mc-station').addEventListener('input', e => { machinesConfig[i].stationCount = parseInt(e.target.value) || 4; persistLineConfig(); });
      card.querySelector('.mc-nc').addEventListener('input', e => { machinesConfig[i].ncPath = e.target.value; persistLineConfig(); });
      card.querySelector('.btn-remove-machine').addEventListener('click', () => {
        machinesConfig.splice(i, 1);
        persistLineConfig();
        renderMachines();
      });
      container.appendChild(card);
    });
  }

  // 添加机床
  document.getElementById('btn-add-machine').addEventListener('click', () => {
    const nextId = machinesConfig.length > 0
      ? String(Math.max(...machinesConfig.map(m => parseInt(m.id) || 0)) + 1)
      : '1';
    machinesConfig.push({ id: nextId, number: nextId, type: '', ip: '', port: '', stationCount: 4, ncPath: '' });
    renderMachines();
  });

  // 收集当前表单配置数据
  function collectConfig() {
    return {
      line: {
        rackType: document.getElementById('cfg-rack-type').value,
        rackCount: document.getElementById('cfg-rack-count').value,
        rackLayers: document.getElementById('cfg-rack-layers').value,
        rackSlots: document.getElementById('cfg-rack-slots').value,
        gripperType: document.getElementById('cfg-gripper-type').value,
        grabMode: document.getElementById('cfg-grab-mode').value,
        shuttleExists: document.getElementById('cfg-shuttle-exists').value,
        shuttleSlots: document.getElementById('cfg-shuttle-slots').value,
        machines: machinesConfig,
        racks: racksConfig
      },
      plc: {
        ip: document.getElementById('cfg-plc-ip').value,
        port: document.getElementById('cfg-plc-port').value,
        signals: {
          rack: {
            openRequest: document.getElementById('cfg-door-open-request').value,
            openAllow: document.getElementById('cfg-door-open-allow').value,
            doorClosed: document.getElementById('cfg-door-closed').value,
            photoChange: document.getElementById('cfg-photoelectric-change').value,
            busy: document.getElementById('cfg-rack-busy').value
          },
          robot: {
            photo: document.getElementById('cfg-robot-photoelectric').value,
            taskStart: document.getElementById('cfg-task-start-signal').value,
            taskDone: document.getElementById('cfg-task-complete-signal').value,
            error: document.getElementById('cfg-robot-error').value,
            safePos: document.getElementById('cfg-robot-safe').value,
            estop: document.getElementById('cfg-emergency-stop').value
          },
          pallet: {
            steelPhoto: document.getElementById('cfg-steel-gripper-photoelectric').value,
            electrodePhoto: document.getElementById('cfg-electrode-gripper-photoelectric').value,
            slotPhoto: document.getElementById('cfg-shuttle-slot-photoelectric').value
          },
          preset: {
            photo: document.getElementById('cfg-preset-photoelectric').value,
            manual: document.getElementById('cfg-preset-manual-signal').value
          },
          cleaner: {
            photo: document.getElementById('cfg-clean-photoelectric').value,
            doorOpen: document.getElementById('cfg-clean-door-open').value,
            doorClosed: document.getElementById('cfg-clean-door-closed').value
          }
        },
        dm: {
          task_number: document.getElementById('cfg-dm-task-number').value,
          robot_task: document.getElementById('cfg-dm-robot-task').value,
          rack_layer_photoelectric: document.getElementById('cfg-dm-rack-photoelectric').value,
          gripper_type: document.getElementById('cfg-dm-gripper-type').value,
          fixture_position: document.getElementById('cfg-dm-fixture-position').value,
          shuttle_part_count: document.getElementById('cfg-dm-shuttle-part-count').value
        }
      },
      database: {
        type: document.getElementById('cfg-db-type').value,
        host: document.getElementById('cfg-db-host').value,
        port: document.getElementById('cfg-db-port').value,
        user: document.getElementById('cfg-db-user').value,
        password: document.getElementById('cfg-db-password').value,
        database: document.getElementById('cfg-db-name').value,
        charset: document.getElementById('cfg-db-charset').value
      },
      emanDatabase: {
        type: document.getElementById('cfg-eman-db-type').value,
        host: document.getElementById('cfg-eman-db-host').value,
        port: document.getElementById('cfg-eman-db-port').value,
        user: document.getElementById('cfg-eman-db-user').value,
        password: document.getElementById('cfg-eman-db-password').value,
        database: document.getElementById('cfg-eman-db-name').value,
        charset: document.getElementById('cfg-eman-db-charset').value
      }
    };
  }

  // 保存配置
  document.getElementById('btn-save-config').addEventListener('click', async () => {
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
  const btnImportExcel = document.getElementById('btn-import-excel');
  const excelFileInput = document.getElementById('excel-file-input');
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
        '线体配置|料架类型': 'cfg-rack-type',
        '线体配置|料架个数': 'cfg-rack-count',
        '线体配置|料架层数': 'cfg-rack-layers',
        '线体配置|料架每层料位数': 'cfg-rack-slots',
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

  // 加载XLSX库（动态）
  function loadXlsxLib() {
    return new Promise((resolve, reject) => {
      if (window.XLSX) { resolve(window.XLSX); return; }
      const s = document.createElement('script');
      s.src = 'https://unpkg.com/xlsx@0.18.5/dist/xlsx.full.min.js';
      s.onload = () => resolve(window.XLSX);
      s.onerror = reject;
      document.head.appendChild(s);
    });
  }

  // 导出Excel
  document.getElementById('btn-export-excel').addEventListener('click', async () => {
    try {
      const XLSX = window.XLSX || (await loadXlsxLib());
      const rows = [
        ['配置项名称', '配置项细分', '具体数值'],
        ['线体配置', '料架类型', document.getElementById('cfg-rack-type').value],
        ['', '料架个数', document.getElementById('cfg-rack-count').value],
        ['', '料架层数', document.getElementById('cfg-rack-layers').value],
        ['', '料架每层料位数', document.getElementById('cfg-rack-slots').value],
        ['', '夹爪类型', document.getElementById('cfg-gripper-type').value],
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
  if (window.ELEMENT) {
    ELEMENT.use(ELEMENT.Message);
    $message = (opts) => {
      if (typeof opts === 'string') opts = { message: opts };
      ELEMENT.Message(opts);
    };
  }

});

// ===== 线体配置页面逻辑 =====
async function loadLineConfig() {
  try {
    const res = await fetch('/api/config');
    if (!res.ok) throw new Error('加载失败');
    const data = await res.json();
    
    // 填充线体配置字段
    if (data.line) {
      const nameInput = document.getElementById('cfg-line-name');
      const typeSelect = document.getElementById('cfg-line-type');
      if (nameInput) nameInput.value = data.line.name || '';
      if (typeSelect) typeSelect.value = data.line.type || 'auto';
    }
    
    // 填充PLC信号配置字段
    if (data.plc) {
      const ipInput = document.getElementById('cfg-plc-ip');
      const portInput = document.getElementById('cfg-plc-port');
      if (ipInput) ipInput.value = data.plc.ip || '';
      if (portInput) portInput.value = data.plc.port || '';
    }
    
    // 填充数据库配置字段
    if (data.database) {
      const typeSelect = document.getElementById('cfg-db-type');
      const connInput = document.getElementById('cfg-db-conn');
      if (typeSelect) typeSelect.value = data.database.type || 'mysql';
      if (connInput) connInput.value = data.database.connectionString || '';
    }
    
    console.log('线体配置加载成功');
  } catch (err) {
    console.error('加载线体配置失败:', err);
    // 不弹窗，避免影响用户体验
  }
}



// 收集配置数据
function collectLineConfig() {
  return {
    line: {
      rackType: document.getElementById('cfg-rack-type')?.value || 'fixed',
      rackCount: parseInt(document.getElementById('cfg-rack-count')?.value) || 1,
      rackLayers: parseInt(document.getElementById('cfg-rack-layers')?.value) || 3,
      rackSlots: parseInt(document.getElementById('cfg-rack-slots')?.value) || 6,
      gripperType: document.getElementById('cfg-gripper-type')?.value || 'fixed-single',
      grabMode: document.getElementById('cfg-grab-mode')?.value || 'A-grab-B-place',
      palletEnabled: document.getElementById('cfg-pallet-enabled')?.value === 'yes',
      palletSlots: parseInt(document.getElementById('cfg-pallet-slots')?.value) || 4,
      racks: racksConfig,
      machines: machinesConfig,
      grippers: grippersConfig
    },
    plc: {
      ip: document.getElementById('cfg-plc-ip')?.value || '',
      port: parseInt(document.getElementById('cfg-plc-port')?.value) || 502,
      signals: {
        robot: {
          photo: document.getElementById('cfg-signal-robot-photo')?.value || '',
          taskStart: document.getElementById('cfg-signal-robot-task-start')?.value || '',
          taskDone: document.getElementById('cfg-signal-robot-task-done')?.value || '',
          taskNumber: document.getElementById('cfg-signal-robot-task-number')?.value || '',
          error: document.getElementById('cfg-signal-robot-error')?.value || '',
          safePos: document.getElementById('cfg-signal-robot-safe-pos')?.value || '',
          estop: document.getElementById('cfg-signal-robot-estop')?.value || ''
        },
        pallet: {
          slotPhoto: document.getElementById('cfg-signal-pallet-slot-photo')?.value || '',
          grabTaskNo: document.getElementById('cfg-signal-pallet-grab-task')?.value || '',
          placeTaskNo: document.getElementById('cfg-signal-pallet-place-task')?.value || '',
          photoStart: document.getElementById('cfg-signal-pallet-photo-start')?.value || ''
        },
        preset: {
          photo: document.getElementById('cfg-signal-preset-photo')?.value || '',
          manual: document.getElementById('cfg-signal-preset-manual')?.value || '',
          openRequest: document.getElementById('cfg-signal-preset-open-request')?.value || '',
          openAllow: document.getElementById('cfg-signal-preset-open-allow')?.value || '',
          grabTaskNo: document.getElementById('cfg-signal-preset-grab-task')?.value || '',
          placeTaskNo: document.getElementById('cfg-signal-preset-place-task')?.value || '',
          doorLock: document.getElementById('cfg-signal-preset-door-lock')?.value || ''
        },
        rotary: {
          rotate: document.getElementById('cfg-signal-rotary-rotate')?.value || '',
          zero: document.getElementById('cfg-signal-rotary-zero')?.value || '',
          grabTaskNo: document.getElementById('cfg-signal-rotary-grab-task')?.value || '',
          placeTaskNo: document.getElementById('cfg-signal-rotary-place-task')?.value || '',
          arrive: document.getElementById('cfg-signal-rotary-arrive')?.value || ''
        },
        cleaner: {
          photo: document.getElementById('cfg-signal-clean-photo')?.value || '',
          grabTaskNo: document.getElementById('cfg-signal-clean-grab-task')?.value || '',
          placeTaskNo: document.getElementById('cfg-signal-clean-place-task')?.value || ''
        }
      },
      dm: {}
    },
    workflow: workflowConfig
  };
}

// 保存配置
async function saveLineConfig() {
  const config = collectLineConfig();
  console.log('[DEBUG] 保存前 machinesConfig:', JSON.stringify(config.line?.machines));

  try {
    const res = await fetch(API_BASE + '/api/config', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(config)
    });
    
    if (res.ok) {
      const result = await res.json();
      if (result.success) {
        toast('配置保存成功', 'success');
      } else {
        toast('保存失败: ' + (result.message || '未知错误'), 'error');
      }
    } else {
      throw new Error('HTTP ' + res.status);
    }
  } catch (err) {
    console.error('保存线体配置失败:', err);
    toast('保存配置失败: ' + err.message, 'error');
  }
}

// 加载配置
async function loadLineConfig() {
  try {
    const res = await fetch(API_BASE + '/api/config');
    if (!res.ok) throw new Error('加载失败');
    const result = await res.json();
    
    if (!result.success) return;
    const data = result.data;
    
    // 线体配置
    const line = data.line || {};
    const rackTypeEl = document.getElementById('cfg-rack-type');
    const rackCountEl = document.getElementById('cfg-rack-count');
    const rackLayersEl = document.getElementById('cfg-rack-layers');
    const rackSlotsEl = document.getElementById('cfg-rack-slots');
    const gripperTypeEl = document.getElementById('cfg-gripper-type');
    const grabModeEl = document.getElementById('cfg-grab-mode');
    const palletEnabledEl = document.getElementById('cfg-pallet-enabled');
    const palletSlotsEl = document.getElementById('cfg-pallet-slots');
    
    if (rackTypeEl) rackTypeEl.value = line.rackType || 'fixed';
    if (rackCountEl) rackCountEl.value = line.rackCount || 1;
    if (rackLayersEl) rackLayersEl.value = line.rackLayers || 3;
    if (rackSlotsEl) rackSlotsEl.value = line.rackSlots || 6;
    if (gripperTypeEl) { gripperTypeEl.value = line.gripperType || 'fixed-single'; onGripperTypeChange(); }
    if (grabModeEl) grabModeEl.value = line.grabMode || 'A-grab-B-place';
    if (palletEnabledEl) { palletEnabledEl.value = line.palletEnabled ? 'yes' : 'no'; updatePalletFields(); }
    if (palletSlotsEl) palletSlotsEl.value = line.palletSlots || 4;
    
    // 料架和机床
    racksConfig = Array.isArray(line.racks) ? line.racks.map(function(r) {
      // 旧数据迁移：type '1'/'2' -> 'electrode'/'steel'
      if (r.type === '1') r.type = 'electrode';
      else if (r.type === '2') r.type = 'steel';
      // 新增字段兜底
      r.openRequest = r.openRequest || '';
      r.openSignal = r.openSignal || '';
      r.doorClosed = r.doorClosed || '';
      r.doorFlag = r.doorFlag || '';
      r.grabStartTaskNo = r.grabStartTaskNo || r.grabTaskNo || '';  // 兼容旧字段名
      r.placeStartTaskNo = r.placeStartTaskNo || r.placeTaskNo || '';
      r.scanStartTaskNo = r.scanStartTaskNo || '';
      return r;
    }) : [];
    machinesConfig = Array.isArray(line.machines) ? line.machines : [];
    grippersConfig = Array.isArray(line.grippers) ? line.grippers : [];
    renderRacks();
    renderMachines();
    renderGrippers();
    // 无料架时添加默认料架卡片
    if (racksConfig.length === 0) addRack();
    
    // PLC配置
    const plc = data.plc || {};
    const plcIpEl = document.getElementById('cfg-plc-ip');
    const plcPortEl = document.getElementById('cfg-plc-port');
    if (plcIpEl) plcIpEl.value = plc.ip || '';
    if (plcPortEl) plcPortEl.value = plc.port || 502;
    
    // PLC信号
    const signals = plc.signals || {};
    const robotSignals = signals.robot || {};
    const palletSignals = signals.pallet || {};
    const presetSignals = signals.preset || {};
    const cleanerSignals = signals.cleaner || {};
    const rotarySignals = signals.rotary || {};

    const signalMap = {
      'cfg-signal-robot-photo': robotSignals.photo,
      'cfg-signal-robot-task-start': robotSignals.taskStart,
      'cfg-signal-robot-task-done': robotSignals.taskDone,
      'cfg-signal-robot-task-number': robotSignals.taskNumber,
      'cfg-signal-robot-error': robotSignals.error,
      'cfg-signal-robot-safe-pos': robotSignals.safePos,
      'cfg-signal-robot-estop': robotSignals.estop,
      'cfg-signal-pallet-slot-photo': palletSignals.slotPhoto,
      'cfg-signal-pallet-grab-task': palletSignals.grabTaskNo,
      'cfg-signal-pallet-place-task': palletSignals.placeTaskNo,
      'cfg-signal-pallet-photo-start': palletSignals.photoStart,
      'cfg-signal-preset-photo': presetSignals.photo,
      'cfg-signal-preset-manual': presetSignals.manual,
      'cfg-signal-preset-open-request': presetSignals.openRequest,
      'cfg-signal-preset-open-allow': presetSignals.openAllow,
      'cfg-signal-preset-grab-task': presetSignals.grabTaskNo,
      'cfg-signal-preset-place-task': presetSignals.placeTaskNo,
      'cfg-signal-preset-door-lock': presetSignals.doorLock,
      'cfg-signal-rotary-rotate': rotarySignals.rotate,
      'cfg-signal-rotary-zero': rotarySignals.zero,
      'cfg-signal-rotary-grab-task': rotarySignals.grabTaskNo,
      'cfg-signal-rotary-place-task': rotarySignals.placeTaskNo,
      'cfg-signal-rotary-arrive': rotarySignals.arrive,
      'cfg-signal-clean-photo': cleanerSignals.photo,
      'cfg-signal-clean-grab-task': cleanerSignals.grabTaskNo,
      'cfg-signal-clean-place-task': cleanerSignals.placeTaskNo
    };
    
    Object.entries(signalMap).forEach(([id, value]) => {
      const el = document.getElementById(id);
      if (el) el.value = value || '';
    });

    // 系统工作流定义
    workflowConfig = Array.isArray(data.workflow) ? data.workflow.map(function(w) {
      return {
        id: w.id != null ? w.id : 0,
        name: w.name || '',
        enabled: w.enabled === true || w.enabled === 1,
        priority: w.priority != null ? w.priority : 0
      };
    }) : [];
    renderWorkflow();

    console.log('线体配置加载成功');
  } catch (err) {
    console.error('加载线体配置失败:', err);
  }
}

// 重置配置
function resetLineConfig() {
  racksConfig = [];
  machinesConfig = [];
  document.querySelectorAll('#lineconfig .config-input').forEach(el => {
    if (el.tagName === 'SELECT') {
      el.selectedIndex = 0;
    } else if (el.type === 'number') {
      el.value = el.defaultValue || '';
    } else {
      el.value = '';
    }
  });
  renderRacks();
  renderMachines();
  toast('已重置', 'info');
}

// 导入Excel
function importLineConfigExcel() {
  document.getElementById('lineconfig-excel-input').click();
}

// 处理Excel导入
async function handleLineConfigExcel(event) {
  const file = event.target.files[0];
  if (!file) return;
  
  try {
    const XLSX = window.XLSX || await loadXLSX();
    if (!XLSX) {
      toast('请先加载xlsx库', 'warning');
      return;
    }
    
    const reader = new FileReader();
    reader.onload = function(e) {
      try {
        const workbook = XLSX.read(e.target.result, { type: 'array' });
        const sheet = workbook.Sheets[workbook.SheetNames[0]];
        const data = XLSX.utils.sheet_to_json(sheet, { header: 1 });
        
        // 解析Excel数据
        parseExcelConfig(data);
        toast('导入成功', 'success');
      } catch (err) {
        console.error('解析Excel失败:', err);
        toast('解析Excel失败', 'error');
      }
    };
    reader.readAsArrayBuffer(file);
  } catch (err) {
    console.error('读取Excel失败:', err);
    toast('读取Excel失败', 'error');
  }
  
  event.target.value = ''; // 重置文件输入
}

// 动态加载xlsx库
async function loadXLSX() {
  return new Promise((resolve) => {
    if (window.XLSX) {
      resolve(window.XLSX);
      return;
    }
    const script = document.createElement('script');
    script.src = 'https://unpkg.com/xlsx@0.18.5/dist/xlsx.full.min.js';
    script.onload = () => resolve(window.XLSX);
    script.onerror = () => resolve(null);
    document.head.appendChild(script);
  });
}

// 解析Excel配置
function parseExcelConfig(data) {
  data.forEach(row => {
    const [configName, subItem, value] = row;
    if (!configName && !subItem) return;
    
    // 线体配置
    if (configName === '线体配置' || subItem) {
      switch (subItem) {
        case '料架类型':
          const rackTypeMap = { '固定料架': 'fixed', '旋转料架': 'rotating' };
          const rackTypeEl = document.getElementById('cfg-rack-type');
          if (rackTypeEl) rackTypeEl.value = rackTypeMap[value] || 'fixed';
          break;
        case '料架个数':
          const rackCountEl = document.getElementById('cfg-rack-count');
          if (rackCountEl && !isNaN(parseInt(value))) rackCountEl.value = parseInt(value);
          break;
        case '料架层数':
          const rackLayersEl = document.getElementById('cfg-rack-layers');
          if (rackLayersEl && !isNaN(parseInt(value))) rackLayersEl.value = parseInt(value);
          break;
        case '料架每层料位数':
          const rackSlotsEl = document.getElementById('cfg-rack-slots');
          if (rackSlotsEl && !isNaN(parseInt(value))) rackSlotsEl.value = parseInt(value);
          break;
        case '夹爪类型':
          const gripperMap = {
            '固定单爪': 'fixed-single',
            '固定双爪': 'fixed-double',
            '快换爪（电极）': 'quick-electrode',
            '快换爪（刀具）': 'quick-tool',
            '快换爪（托盘）': 'quick-pallet'
          };
          const gripperEl = document.getElementById('cfg-gripper-type');
          if (gripperEl) gripperEl.value = gripperMap[value] || 'fixed-single';
          break;
        case '抓取方式':
          const grabMap = { 'A抓B放': 'A-grab-B-place', 'B抓A放': 'B-grab-A-place' };
          const grabEl = document.getElementById('cfg-grab-mode');
          if (grabEl) grabEl.value = grabMap[value] || 'A-grab-B-place';
          break;
        case '随行台有无':
          const palletEl = document.getElementById('cfg-pallet-enabled');
          if (palletEl) palletEl.value = value === '有' ? 'yes' : 'no';
          if (typeof updatePalletFields === 'function') updatePalletFields();
          break;
        case '随行台料位数':
          const palletSlotsEl = document.getElementById('cfg-pallet-slots');
          if (palletSlotsEl && !isNaN(parseInt(value))) palletSlotsEl.value = parseInt(value);
          break;
      }
    }
  });
}

// 导出Excel
async function exportLineConfigExcel() {
  try {
    const XLSX = window.XLSX || await loadXLSX();
    if (!XLSX) {
      toast('请先加载xlsx库', 'warning');
      return;
    }
    
    const config = collectLineConfig();
    const data = [
      ['配置项名称', '配置项细分', '具体数值'],
      ['线体配置', '料架类型', config.line.rackType === 'fixed' ? '固定料架' : '旋转料架'],
      ['', '料架个数', config.line.rackCount],
      ['', '料架层数', config.line.rackLayers],
      ['', '料架每层料位数', config.line.rackSlots],
      ['', '夹爪类型', getGripperName(config.line.gripperType)],
      ['', '抓取方式', config.line.grabMode === 'A-grab-B-place' ? 'A抓B放' : 'B抓A放'],
      ['', '随行台有无', config.line.palletEnabled ? '有' : '无'],
      ['', '随行台料位数', config.line.palletSlots],
      ['', '机床信息', machinesConfig.map(m => `${m.number}/${m.ip}/${m.brand}`).join('; ')],
      ['PLC信号', '料架', '料架开门请求'],
      ['', '', '允许开门信号'],
      ['', '', '料架门关信号'],
      ['', '', '各层光电变化信号'],
      ['', '', '料架忙碌信号'],
      ['', '机器人', '机器人光电信号'],
      ['', '', '机器人任务开始信号'],
      ['', '', '机器人任务完成信号'],
      ['', '', '机器人报错'],
      ['', '', '机器人安全位置信号'],
      ['', '', '急停触发'],
      ['', '随行台', '钢件快换夹爪料位光电信号'],
      ['', '', '电极快换夹爪料位光电信号'],
      ['', '', '各料位光电信号'],
      ['', '预设台', '预设台光电信号'],
      ['', '', '预设台人工操作信号'],
      ['', '清洁站', '清洁站清洁位光电信号'],
      ['', '', '清洁站门开到位信号'],
      ['', '', '清洁站门关到位信号'],
      ['数据库', '本地数据库配置', `类型:${config.database.local.type}, 连接:${config.database.local.connectionString}`],
      ['', 'eman数据库配置', `类型:${config.database.eman.type}, 连接:${config.database.eman.connectionString}`]
    ];
    
    const ws = XLSX.utils.aoa_to_sheet(data);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, '线体配置');
    XLSX.writeFile(wb, '线体配置导出_' + new Date().toISOString().slice(0, 10) + '.xlsx');
    toast('导出成功', 'success');
  } catch (err) {
    console.error('导出Excel失败:', err);
    toast('导出失败', 'error');
  }
}

function getGripperName(type) {
  const map = {
    'fixed-single': '固定单爪',
    'fixed-double': '固定双爪',
    'quick-electrode': '快换爪（电极）',
    'quick-tool': '快换爪（刀具）',
    'quick-pallet': '快换爪（托盘）'
  };
  return map[type] || '固定单爪';
}

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
  
  // 随行台有无 -> 禁用/启用随行台相关配置
  const palletEnabled = document.getElementById('cfg-pallet-enabled');
  const palletSlotsField = document.getElementById('pallet-slots-field');
  const palletSection = document.getElementById('pallet-signals-section');
  const palletSlotsEl = document.getElementById('cfg-pallet-slots');
  const palletInputs = [
    document.getElementById('cfg-pallet-slots'),
    document.getElementById('cfg-signal-pallet-slot-photo'),
    document.getElementById('cfg-signal-pallet-grab-task'),
    document.getElementById('cfg-signal-pallet-place-task'),
    document.getElementById('cfg-signal-pallet-photo-start')
  ];
  var palletSavedValues = {}; // 切换前保存的值

  function updatePalletFields() {
    var isNo = palletEnabled.value === 'no';
    palletInputs.forEach(function(el) {
      if (!el) return;
      if (isNo) {
        // 保存当前值
        palletSavedValues[el.id] = el.value;
        el.disabled = true;
        el.value = '0';
        el.style.opacity = '0.5';
      } else {
        el.disabled = false;
        el.style.opacity = '';
        // 还原旧值，若为空则用0
        var saved = palletSavedValues[el.id];
        el.value = (saved !== undefined && saved !== '0') ? saved : (el.id === 'cfg-pallet-slots' ? '4' : '');
      }
    });
    // palletSlotsField visibility handled by existing css
  }

  if (palletEnabled) {
    palletEnabled.addEventListener('change', updatePalletFields);
    // 初始化时也执行一次
    updatePalletFields();
  }
  
  // 初始化：从 localStorage 恢复配置并渲染
  restoreLineConfig();
  renderRacks();
  renderMachines();
});
