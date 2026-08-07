// ==================== logs ====================

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

