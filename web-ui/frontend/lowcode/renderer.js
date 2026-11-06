/**
 * NodeRenderer —— 节点属性面板渲染器
 *
 * 作用：把 node-types.json 里的字段声明，变成长出真实的表单控件。
 * 依赖：无。原生 JS，直接 <script> 引入即可。
 *
 * 用法：
 *   NodeRenderer.render(el, NodeRenderer.fieldsFor(meta, 'scan'), values, onChange)
 *   var values = NodeRenderer.read(el);
 *
 * 加一种新参数类型 = 在 types 里加一个对象（render + read），不改其它任何地方。
 */
(function (global) {
  'use strict';

  var CSS_ID = 'nr-style';
  var CSS = [
    '.nr-form{font-size:13px;color:rgba(0,0,0,.88)}',
    '.nr-row{margin-bottom:14px}',
    '.nr-row:last-child{margin-bottom:0}',
    '.nr-label{display:block;font-size:12px;color:rgba(0,0,0,.65);margin-bottom:5px}',
    '.nr-req{color:#ff4d4f;margin-left:2px}',
    '.nr-ctrl{display:flex;align-items:center;gap:6px}',
    '.nr-input,.nr-select{',
    '  width:100%;height:30px;padding:0 10px;font-size:13px;color:rgba(0,0,0,.88);',
    '  background:#fff;border:1px solid #d9d9d9;border-radius:6px;outline:none;',
    '  transition:border-color .15s,box-shadow .15s;font-family:inherit;',
    '}',
    '.nr-select{cursor:pointer;padding-right:6px}',
    '.nr-input:hover,.nr-select:hover{border-color:#4096ff}',
    '.nr-input:focus,.nr-select:focus{border-color:#1677ff;box-shadow:0 0 0 2px rgba(5,145,255,.1)}',
    '.nr-unit{flex:0 0 auto;font-size:12px;color:rgba(0,0,0,.45);min-width:24px}',
    '.nr-hint{font-size:11px;color:rgba(0,0,0,.45);margin-top:4px;line-height:1.5}',
    '.nr-switch{position:relative;display:inline-block;width:34px;height:18px;flex:0 0 34px}',
    '.nr-switch input{opacity:0;width:0;height:0;position:absolute}',
    '.nr-switch span{position:absolute;inset:0;background:#bfbfbf;border-radius:9px;transition:background .18s;cursor:pointer}',
    '.nr-switch span::after{content:"";position:absolute;width:14px;height:14px;left:2px;top:2px;background:#fff;border-radius:50%;transition:transform .18s}',
    '.nr-switch input:checked + span{background:#1677ff}',
    '.nr-switch input:checked + span::after{transform:translateX(16px)}',
    '.nr-switch-text{font-size:12px;color:rgba(0,0,0,.65)}'
  ].join('');

  function injectCSS() {
    if (document.getElementById(CSS_ID)) return;
    var s = document.createElement('style');
    s.id = CSS_ID;
    s.textContent = CSS;
    document.head.appendChild(s);
  }

  function esc(s) {
    return String(s == null ? '' : s)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  /* ---------------- 类型注册表 ---------------- */
  /* 每个类型两个方法：render(field, value) 返回 HTML；read(root) 从 DOM 解析返回值 */

  var types = {

    number: {
      render: function (f, v) {
        var val = (v === undefined || v === null || v === '') ? (f.default == null ? '' : f.default) : v;
        var attrs = '';
        if (f.min != null) attrs += ' min="' + f.min + '"';
        if (f.max != null) attrs += ' max="' + f.max + '"';
        return '<div class="nr-ctrl">'
          + '<input class="nr-input" type="number" data-role="ctrl" value="' + esc(val) + '"' + attrs + '>'
          + (f.unit ? '<span class="nr-unit">' + esc(f.unit) + '</span>' : '')
          + '</div>';
      },
      read: function (root) {
        var el = root.querySelector('[data-role="ctrl"]');
        if (!el) return null;
        if (el.value === '') return null;
        var n = Number(el.value);
        return isNaN(n) ? null : n;
      }
    },

    text: {
      render: function (f, v) {
        var val = (v === undefined || v === null) ? (f.default == null ? '' : f.default) : v;
        return '<div class="nr-ctrl">'
          + '<input class="nr-input" type="text" data-role="ctrl" value="' + esc(val) + '"'
          + (f.placeholder ? ' placeholder="' + esc(f.placeholder) + '"' : '') + '>'
          + '</div>';
      },
      read: function (root) {
        var el = root.querySelector('[data-role="ctrl"]');
        return el ? el.value : '';
      }
    },

    enum: {
      render: function (f, v) {
        var val = (v === undefined || v === null || v === '') ? f.default : v;
        var opts = '';
        (f.options || []).forEach(function (o) {
          var ov = (typeof o === 'object') ? o.value : o;
          var ol = (typeof o === 'object') ? o.label : o;
          opts += '<option value="' + esc(ov) + '"' + (String(ov) === String(val) ? ' selected' : '') + '>' + esc(ol) + '</option>';
        });
        return '<div class="nr-ctrl"><select class="nr-select" data-role="ctrl">' + opts + '</select></div>';
      },
      read: function (root) {
        var el = root.querySelector('[data-role="ctrl"]');
        return el ? el.value : null;
      }
    },

    switch: {
      render: function (f, v) {
        var on = (v === undefined || v === null) ? !!f.default : !!v;
        return '<div class="nr-ctrl">'
          + '<label class="nr-switch"><input type="checkbox" data-role="ctrl"' + (on ? ' checked' : '') + '><span></span></label>'
          + '<span class="nr-switch-text">' + (on ? '开' : '关') + '</span>'
          + '</div>';
      },
      read: function (root) {
        var el = root.querySelector('[data-role="ctrl"]');
        return el ? el.checked : false;
      }
    },

    path: {
      render: function (f, v) {
        var val = (v === undefined || v === null) ? (f.default == null ? '' : f.default) : v;
        return '<div class="nr-ctrl">'
          + '<input class="nr-input" type="text" data-role="ctrl" value="' + esc(val) + '" placeholder="相对路径，如 O0011.nc">'
          + '</div>';
      },
      read: function (root) {
        var el = root.querySelector('[data-role="ctrl"]');
        return el ? el.value : '';
      }
    }
  };

  /* ---------------- 对外接口 ---------------- */

  /**
   * 取某个节点的完整字段列表 = 通用字段 + 节点特有字段
   */
  function fieldsFor(meta, key) {
    if (!meta) return [];
    var common = meta.commonFields || [];
    var node = null;
    (meta.nodes || []).forEach(function (n) { if (n.key === key) node = n; });
    if (!node) return common.slice();
    return common.concat(node.fields || []);
  }

  /**
   * 渲染表单
   * @param {Element} container 容器
   * @param {Array}   fields    fieldsFor() 的结果
   * @param {Object}  values    当前值，缺省的用字段 default
   * @param {Function} onChange 变更回调 (name, value, allValues)
   */
  function render(container, fields, values, onChange) {
    injectCSS();
    values = values || {};
    var html = '<div class="nr-form">';

    fields.forEach(function (f) {
      var t = types[f.type] || types.text;
      html += '<div class="nr-row" data-name="' + esc(f.name) + '" data-type="' + esc(f.type) + '">';
      html += '<label class="nr-label">' + esc(f.label || f.name)
        + (f.required ? '<span class="nr-req">*</span>' : '') + '</label>';
      html += t.render(f, values[f.name]);
      if (f.hint) html += '<div class="nr-hint">' + esc(f.hint) + '</div>';
      html += '</div>';
    });

    html += '</div>';
    container.innerHTML = html;

    if (typeof onChange === 'function') {
      container.addEventListener('change', function (e) {
        var row = e.target.closest ? e.target.closest('.nr-row') : null;
        if (!row) return;
        var name = row.getAttribute('data-name');
        var type = row.getAttribute('data-type');
        var t = types[type] || types.text;
        var val = t.read(row);

        if (type === 'switch') {
          var txt = row.querySelector('.nr-switch-text');
          if (txt) txt.textContent = val ? '开' : '关';
        }
        onChange(name, val, read(container));
      });
    }
    return container;
  }

  /**
   * 从表单读回全部值
   */
  function read(container) {
    var out = {};
    var rows = container.querySelectorAll('.nr-row');
    for (var i = 0; i < rows.length; i++) {
      var row = rows[i];
      var name = row.getAttribute('data-name');
      var type = row.getAttribute('data-type');
      var t = types[type] || types.text;
      out[name] = t.read(row);
    }
    return out;
  }

  /**
   * 注册新类型（运行期间也可以加）
   */
  function register(type, impl) {
    if (!impl || typeof impl.render !== 'function' || typeof impl.read !== 'function') {
      throw new Error('类型实现必须同时提供 render 和 read');
    }
    types[type] = impl;
  }

  global.NodeRenderer = {
    render: render,
    read: read,
    fieldsFor: fieldsFor,
    register: register,
    types: types,
    version: '1.0.0'
  };

})(window);
