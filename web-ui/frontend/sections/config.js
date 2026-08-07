// ==================== config ====================

  async function loadConfig() {
    try {
      const res = await apiGet('/api/config');
      if (!res.success) return;
      const d = res.data;

      // 线体配置
      const line = d.line || {};
      const setVal = (id, val) => { const el = document.getElementById(id); if (el) el.value = val; };
      setVal('cfg-rack-type', line.rackType || '固定料架');
      setVal('cfg-rack-count', line.rackCount || '');
      setVal('cfg-rack-layers', line.rackLayers || '');
      setVal('cfg-rack-slots', line.rackSlots || '');
      setVal('cfg-gripper-type', line.gripperType || '固定单爪');
      setVal('cfg-grab-mode', line.grabMode || 'A抓B放');
      setVal('cfg-pallet-enabled', line.palletEnabled ? 'yes' : 'no');
      setVal('cfg-pallet-slots', line.palletSlots || '4');
      if (typeof updatePalletFields === 'function') updatePalletFields();

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
      setVal('cfg-plc-ip', plc.ip || '');
      setVal('cfg-plc-port', plc.port || '');
      // rack 信号
      setVal('cfg-door-open-request', rackSignals.openRequest || '');
      setVal('cfg-door-open-allow', rackSignals.openAllow || '');
      setVal('cfg-door-closed', rackSignals.doorClosed || '');
      setVal('cfg-photoelectric-change', rackSignals.photoChange || '');
      setVal('cfg-rack-busy', rackSignals.busy || '');
      // robot 信号
      setVal('cfg-robot-photoelectric', robotSignals.photo || '');
      setVal('cfg-task-start-signal', robotSignals.taskStart || '');
      setVal('cfg-task-complete-signal', robotSignals.taskDone || '');
      setVal('cfg-robot-error', robotSignals.error || '');
      setVal('cfg-robot-safe', robotSignals.safePos || '');
      setVal('cfg-emergency-stop', robotSignals.estop || '');
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
      setVal('cfg-preset-photoelectric', presetSignals.photo || '');
      setVal('cfg-preset-manual-signal', presetSignals.manual || '');
      // cleaner 信号
      setVal('cfg-clean-photoelectric', cleanerSignals.photo || '');
      setVal('cfg-clean-door-open', cleanerSignals.doorOpen || '');
      setVal('cfg-clean-door-closed', cleanerSignals.doorClosed || '');

      // PLC DM区
      const dm = plc.dm || {};
      setVal('cfg-dm-task-number', dm.task_number || '');
      setVal('cfg-dm-robot-task', dm.robot_task || '');
      setVal('cfg-dm-rack-photoelectric', dm.rack_layer_photoelectric || '');
      setVal('cfg-dm-gripper-type', dm.gripper_type || '');
      setVal('cfg-dm-fixture-position', dm.fixture_position || '');
      setVal('cfg-dm-shuttle-part-count', dm.shuttle_part_count || '');

      // 数据库
      const db = d.database || {};
      setVal('cfg-db-type', db.type || 'mysql');
      setVal('cfg-db-host', db.host || '');
      setVal('cfg-db-port', db.port || '');
      setVal('cfg-db-user', db.user || '');
      setVal('cfg-db-password', db.password || '');
      setVal('cfg-db-name', db.database || '');
      setVal('cfg-db-charset', db.charset || '');

      // eman数据库
      const eman = d.emanDatabase || {};
      setVal('cfg-eman-db-type', eman.type || 'mysql');
      setVal('cfg-eman-db-host', eman.host || '');
      setVal('cfg-eman-db-port', eman.port || '');
      setVal('cfg-eman-db-user', eman.user || '');
      setVal('cfg-eman-db-password', eman.password || '');
      setVal('cfg-eman-db-name', eman.database || '');
      setVal('cfg-eman-db-charset', eman.charset || '');

    } catch (e) {
      console.warn('配置加载失败', e);
    }
  }

function renderMachines() {
    const container = document.getElementById('machine-list');
    container.innerHTML = '';
    machinesConfig.forEach((m, i) => {
      const card = document.createElement('div');
      card.className = 'machine-card';
      var isSiemens = (m.type || '').toLowerCase() === 'siemense' || (m.type || '').toLowerCase() === 'siemens';
      var html = '';
      html += '<div class="config-field"><label>机床编号</label><input class="config-input mc-number" value="' + escapeHtml(m.number || '') + '" placeholder="如: 1" /></div>';
      html += '<div class="config-field"><label>机床品牌</label><input class="config-input mc-type" value="' + escapeHtml(m.type || '') + '" placeholder="如: fanuc" /></div>';
      html += '<div class="config-field"><label>机床IP</label><input class="config-input mc-ip" value="' + escapeHtml(m.ip || '') + '" placeholder="如: 192.168.1.101" /></div>';
      html += '<div class="config-field"><label>通讯端口</label><input class="config-input mc-port" value="' + escapeHtml(m.port || '') + '" placeholder="如: 8193" /></div>';
      html += '<div class="config-field"><label>工位数</label><input class="config-input mc-station" type="number" value="' + (m.stationCount || 4) + '" min="1" /></div>';
      html += '<div class="config-field"><label>NC路径</label><input class="config-input mc-nc" value="' + escapeHtml(m.ncPath || '') + '" placeholder="如: C:\\NC\\Mac1" /></div>';
      if (isSiemens) {
        html += '<div class="config-field"><label>会话URL</label><input class="config-input mc-session-url" value="' + escapeHtml(m.sessionUrl || '') + '" placeholder="如: opc.tcp://192.168.1.1:4840" /></div>';
        html += '<div class="config-field"><label>登录用户名</label><input class="config-input mc-username" value="' + escapeHtml(m.username || '') + '" placeholder="OPC UA 用户名" /></div>';
        html += '<div class="config-field"><label>登录密码</label><input class="config-input mc-password" type="password" value="' + escapeHtml(m.password || '') + '" placeholder="OPC UA 密码" /></div>';
      }
      html += '<button class="btn-remove-machine" title="删除机床">✕</button>';
      card.innerHTML = html;
      card.querySelector('.mc-number').addEventListener('input', e => { machinesConfig[i].number = e.target.value; persistLineConfig(); });
      card.querySelector('.mc-type').addEventListener('input', e => { machinesConfig[i].type = e.target.value; persistLineConfig(); renderMachines(); });
      card.querySelector('.mc-ip').addEventListener('input', e => { machinesConfig[i].ip = e.target.value; persistLineConfig(); });
      card.querySelector('.mc-port').addEventListener('input', e => { machinesConfig[i].port = e.target.value; persistLineConfig(); });
      card.querySelector('.mc-station').addEventListener('input', e => { machinesConfig[i].stationCount = parseInt(e.target.value) || 4; persistLineConfig(); });
      card.querySelector('.mc-nc').addEventListener('input', e => { machinesConfig[i].ncPath = e.target.value; persistLineConfig(); });
      if (isSiemens) {
        card.querySelector('.mc-session-url').addEventListener('input', e => { machinesConfig[i].sessionUrl = e.target.value; persistLineConfig(); });
        card.querySelector('.mc-username').addEventListener('input', e => { machinesConfig[i].username = e.target.value; persistLineConfig(); });
        card.querySelector('.mc-password').addEventListener('input', e => { machinesConfig[i].password = e.target.value; persistLineConfig(); });
      }
      card.querySelector('.btn-remove-machine').addEventListener('click', () => {
        machinesConfig.splice(i, 1);
        persistLineConfig();
        renderMachines();
      });
      container.appendChild(card);
    });
  }

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
    if (palletEnabledEl) {
      palletEnabledEl.value = line.palletEnabled ? 'yes' : 'no';
      if (typeof updatePalletFields === 'function') updatePalletFields();
    }
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

function importLineConfigExcel() {
  document.getElementById('lineconfig-excel-input').click();
}

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

  function updatePalletFields() {
    var palletEnabled = document.getElementById('cfg-pallet-enabled');
    if (!palletEnabled) return;
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

