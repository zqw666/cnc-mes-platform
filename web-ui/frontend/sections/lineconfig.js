// ==================== 线体配置 — 全局函数/数据 ====================
// 这些函数必须在 DOMContentLoaded 外，供 HTML onclick 访问

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
    ncPath: data.ncPath || '',
    sessionUrl: data.sessionUrl || '',
    username: data.username || '',
    password: data.password || ''
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
  if (workflowConfig.length === 0) {
    container.innerHTML = '<tr><td colspan="5" style="padding:20px;text-align:center;color:#8c9bb5;">暂无步骤，点击上方"+ 添加步骤"按钮添加</td></tr>';
    return;
  }
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
