// ==================== tasks ====================

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

