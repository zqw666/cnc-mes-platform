// ==================== shelves ====================

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
        // racktype 为数字：1=电极 2=钢件 3=刀具 4=托盘 5=铝板电极
        const racktypeNum = RACKTYPE_MAP[type];
        const rows = allRows.filter(r => Number(r.racktype) === racktypeNum).sort((a, b) => a.id - b.id);
        shelves[type] = [];
        for (let i = 1; i <= DEFAULT_SLOTS[type]; i++) {
          const row = rows[i - 1];
          shelves[type].push(row ? {
            id: Number(row.id), rfid: row.rfid || '', posstatus: Number(row.posstatus || 0),
            workstate: Number(row.workstate || 0), scanstatus: Number(row.scanstatus || 0),
            priority: Number(row.priority || 0), racknum: Number(row.racknum || 1),
            workpiecename: row.workpiecename || '', activated: false, selected: false
          } : { id: i, rfid: '', posstatus: 0, workstate: 0, scanstatus: 0, priority: 0, racknum: 1, workpiecename: '', activated: false, selected: false });
        }
      });
    } catch (e) {
      console.warn('料架加载失败，使用默认空槽位', e);
      fillDefault();
    }
  }

  function shelfStatus(row, type) {
    // 加工中/加工完成优先（工件在机床，料位 posstatus=1）
    if (row.workstate === 1) return 'processing';
    if (row.workstate === 2) return 'completed';
    // 有工件在料架（posstatus=2）：读 scanstatus
    if (row.posstatus === 2) {
      if (row.scanstatus === 0) return 'to-scan';     // 待扫描
      if (row.scanstatus === 2) return 'scan-failed'; // 扫描失败
      return 'pending';                                // 扫描成功 → 待加工
    }
    // 无工件：posstatus=0 禁用，posstatus=1 无料
    return row.posstatus === 0 ? 'disabled' : 'no-material';
  }

  function getStatusText(status) {
    const map = {
      'processing': '加工中',
      'pending': '待加工',
      'completed': '已完成',
      'to-scan': '待扫描',
      'no-material': '无料',
      'scan-failed': '扫描失败',
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
      const hasWorkpiece = !!(shelf.workstate > 0 || shelf.posstatus === 2);
      let infoRows;
      if (hasWorkpiece) {
        // 有工件：显示工件监控编号 + 状态
        const wsText = getStatusText(status);
        infoRows =
          '<div class="shelf-info-row"><span class="shelf-info-label">监控:</span><span class="shelf-info-value shelf-rfid">' + (shelf.rfid || '-') + '</span></div>' +
          '<div class="shelf-info-row"><span class="shelf-info-label">状态:</span><span class="shelf-info-value">' + wsText + '</span></div>';
      } else {
        // 无工件：显示料位状态（禁用 or 无料）
        const psText = shelf.posstatus === 0 ? '禁用' : '无料';
        infoRows =
          '<div class="shelf-info-row"><span class="shelf-info-label">料位状态:</span><span class="shelf-info-value">' + psText + '</span></div>';
      }
      item.innerHTML = `
        <input type="checkbox" class="shelf-checkbox" data-id="${shelf.id}" ${shelf.selected ? 'checked' : ''}>
        <div class="shelf-position">#${shelf.id}</div>
        ${infoRows}
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
          s.selected = false;   // 重置选中单选框（料位保持激活状态）
          if (status === 'disable') { s.posstatus = 0; s.workstate = 0; s.scanstatus = 0; }        // 禁用
          else if (status === 'no-material') { s.posstatus = 1; s.workstate = 0; s.scanstatus = 0; } // 无料
          else if (status === 'urgent') s.priority = 1;
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

