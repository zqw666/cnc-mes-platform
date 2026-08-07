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
        const rows = allRows.filter(r => (r.racktype || '').toString().toLowerCase() === type).sort((a, b) => a.id - b.id);
        shelves[type] = [];
        for (let i = 1; i <= DEFAULT_SLOTS[type]; i++) {
          const row = rows[i - 1];
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

