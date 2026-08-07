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


// ==================== 初始化辅助函数 ====================

// 全局 $message 引用（由 Element UI 注入）
var $message = null;

function toast(msg, type) {
  if ($message) { $message({ message: msg, type: type || 'info', duration: 2500 }); return; }
  console.log('[' + type + ']', msg);
}

function updateTime() {
  var now = new Date();
  document.getElementById('datetime').textContent =
    now.getFullYear() + '-' + pad(now.getMonth() + 1) + '-' + pad(now.getDate()) +
    ' ' + pad(now.getHours()) + ':' + pad(now.getMinutes()) + ':' + pad(now.getSeconds());
}

function updateNetwork() {
  var online = navigator.onLine;
  var el = document.getElementById('net-indicator');
  if (el) {
    el.textContent = online ? '在线' : '离线';
    el.className = online ? 'online' : 'offline';
  }
}

function updateSidebarLeftVar() {
  var sidebar = document.getElementById('sidebar');
  if (!sidebar) return;
  var collapsed = sidebar.classList.contains('collapsed');
  var val = collapsed
    ? getComputedStyle(document.documentElement).getPropertyValue('--sidebar-collapsed')
    : getComputedStyle(document.documentElement).getPropertyValue('--sidebar-width');
  document.documentElement.style.setProperty('--sidebar-left', val.trim());
}
