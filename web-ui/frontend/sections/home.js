// ==================== home ====================

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

      // 三台机床状态 -> 页面状态文字
      for (let i = 0; i < 3; i++) {
        const mi = machineData[i] || {};
        let text;
        if (mi.status === 0) text = '脱机';
        else if (mi.status === 1) {
          if (mi.workstate === 1) text = '运行';
          else if (mi.workstate === 2 || mi.error_message) text = '停止';
          else text = '正常';
        } else {
          text = '正常';
        }
        const el = document.getElementById('machine' + (i + 1) + '-status');
        if (el) setStatus(el, text);
        const card = document.getElementById('machine' + (i + 1) + '-card');
        if (card) card.classList.toggle('offline', mi.status === 0);
      }
      setStatus(document.getElementById('robot-status'), '正常');

      if (!connected) {
        connected = true;
        updateNetwork();
      }
    } catch (e) {
      connected = false;
      updateNetwork();
    }
  }

  async function syncWorkflowStatus() {
    try {
      const res = await apiGet('/api/workflow/status');
      if (!res.success) return;
      const prev = workflowState;
      workflowState = res.running ? (res.paused ? 'paused' : 'running') : 'idle';

      // 状态变化时更新设备显示（脱机机床保持“脱机”并置灰）
      if (workflowState !== prev) {
        const wfText = workflowState === 'running' ? '运行' : workflowState === 'paused' ? '暂停' : '正常';
        for (let i = 1; i <= 3; i++) {
          const mi = machineData[i - 1] || {};
          const offline = mi.status === 0;
          setStatus(document.getElementById('machine' + i + '-status'), offline ? '脱机' : wfText);
          const card = document.getElementById('machine' + i + '-card');
          if (card) card.classList.toggle('offline', offline);
        }
        setStatus(document.getElementById('robot-status'), wfText);
      }
    } catch (e) {
      // 网络异常，保持上次状态
    }
  }

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
    if (log.display) {
      content = `[${log.display}] ${content}`;
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
    if (log.display) {
      content = `[${log.display}] ${content}`;
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

  function clearHomeLog() {
    const homeLogBody = document.getElementById('home-log-tbody');
    if (homeLogBody) {
      homeLogBody.innerHTML = '';
    }
  }

  // ==================== 机床卡片单击/双击：双击脱机、脱机后单击加入线体 ====================
  let machineClickTimers = [null, null, null];

  function setupMachineCardEvents() {
    for (let i = 1; i <= 3; i++) {
      const card = document.getElementById('machine' + i + '-card');
      if (!card) continue;
      const idx = i - 1;
      card.addEventListener('click', () => {
        if (machineClickTimers[idx]) clearTimeout(machineClickTimers[idx]);
        machineClickTimers[idx] = setTimeout(() => {
          const m = machineData[idx];
          if (m && m.status === 0) confirmAddMachine(m.id);
        }, 300);
      });
      card.addEventListener('dblclick', () => {
        if (machineClickTimers[idx]) { clearTimeout(machineClickTimers[idx]); machineClickTimers[idx] = null; }
        const m = machineData[idx];
        if (m) offlineMachine(m.id);
      });
    }
  }

  async function offlineMachine(id) {
    try {
      const res = await apiPost('/api/machine/offline', { id });
      if (res.success) {
        toast('机床已脱机', 'success');
        refreshDashboard();
      } else {
        toast('脱机失败: ' + (res.message || '未知错误'), 'warning');
      }
    } catch (e) {
      toast('脱机失败: ' + e.message, 'error');
    }
  }

  function askConfirm(message) {
    if (window.ELEMENT && ELEMENT.MessageBox && typeof ELEMENT.MessageBox.confirm === 'function') {
      return ELEMENT.MessageBox.confirm(message, '提示', {
        confirmButtonText: '确定',
        cancelButtonText: '取消',
        type: 'info'
      }).then(() => true).catch(() => false);
    }
    return Promise.resolve(confirm(message));
  }

  async function confirmAddMachine(id) {
    const ok = await askConfirm('是否将机床加入线体？');
    if (!ok) return;
    try {
      const res = await apiPost('/api/machine/enable', { id });
      if (res.success) {
        toast('机床已加入线体', 'success');
        refreshDashboard();
      } else {
        toast('加入线体失败: ' + (res.message || '未知错误'), 'warning');
      }
    } catch (e) {
      toast('加入线体失败: ' + e.message, 'error');
    }
  }

