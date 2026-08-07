// ==================== 通用工具函数 ====================

function pad(n) { return n < 10 ? '0' + n : n; }

function setStatus(el, state) {
  el.classList.remove('normal', 'running', 'paused', 'stopped', 'auto');
  var map = { '正常': 'normal', '运行': 'running', '暂停': 'paused', '停止': 'stopped', '自动': 'auto' };
  if (map[state]) el.classList.add(map[state]);
  el.textContent = state;
}

function escapeHtml(text) {
  var div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}
