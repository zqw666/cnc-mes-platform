const express = require('express');
const mysql = require('mysql2/promise');
const cors = require('cors');
const fs = require('fs');
const WebSocket = require('ws');
const http = require('http');

const app = express();
app.use(cors());
app.use(express.json());

// Load config
let config;
try {
  config = JSON.parse(fs.readFileSync('config.json', 'utf8'));
} catch (err) {
  console.error('Error loading config.json:', err);
  process.exit(1);
}

const dbConfig = config.database;

// Create connection pool
const pool = mysql.createPool(dbConfig);

// ==================== WebSocket 设置 ====================

const server = http.createServer(app);
const wss = new WebSocket.Server({ server });

// 存储所有连接的客户端
const clients = new Set();

wss.on('connection', (ws) => {
  console.log('WebSocket client connected');
  clients.add(ws);

  ws.on('close', () => {
    console.log('WebSocket client disconnected');
    clients.delete(ws);
  });

  ws.on('error', (err) => {
    console.error('WebSocket error:', err);
    clients.delete(ws);
  });
});

// 广播消息给所有前端客户端
function broadcastToFrontend(data) {
  const message = JSON.stringify(data);
  clients.forEach((ws) => {
    if (ws.readyState === WebSocket.OPEN) {
      ws.send(message);
    }
  });
}

// 广播日志给所有前端客户端
function broadcastLog(level, step, message, details = {}) {
  broadcastToFrontend({
    type: 'log',
    data: {
      level,      // INFO, WARN, ERROR, SUCCESS
      step,       // 分包名称
      message,    // 日志内容
      details,    // 额外详情
      timestamp: new Date().toISOString()
    }
  });
}
  const message = JSON.stringify(data);
  clients.forEach((ws) => {
    if (ws.readyState === WebSocket.OPEN) {
      ws.send(message);
    }
  });
}

// ==================== 机床监控接口 ====================

// GET /api/dashboard
app.get('/api/dashboard', async (req, res) => {
  try {
    const [machineRows] = await pool.execute(`
      SELECT id, machinename, workstate, status,
             current_workpiece, processing_duration,
             machine_status, error_message
      FROM machine
    `);

    const machines = machineRows.map(row => ({
      id: row.id,
      machinename: row.machinename || '',
      workstate: row.workstate || 0,
      status: row.status || 0,
      current_workpiece: row.current_workpiece || '',
      processing_duration: row.processing_duration || 0,
      machine_status: row.machine_status || 0,
      error_message: row.error_message || ''
    }));

    const today = new Date().toISOString().slice(0, 10);
    const [statRows] = await pool.execute(`
      SELECT
        COUNT(CASE WHEN process_status = 2 THEN 1 END) AS processed,
        COUNT(CASE WHEN process_status IN (0,1) THEN 1 END) AS unprocessed
      FROM processlog
      WHERE DATE(process_start_time) = ?
    `, [today]);

    res.json({
      success: true,
      data: {
        machines,
        today_processed: statRows[0].processed || 0,
        today_unprocessed: statRows[0].unprocessed || 0,
        today
      }
    });
  } catch (err) {
    console.error('[/api/dashboard]', err);
    res.status(500).json({ success: false, message: '数据库错误' });
  }
});

// POST /api/machine/:action
app.post('/api/machine/:action', async (req, res) => {
  try {
    const { action } = req.params;
    const { id } = req.body || {};

    if (!['start', 'pause', 'stop'].includes(action)) {
      return res.status(400).json({ success: false, message: '未知操作' });
    }

    if (action === 'start') {
      await pool.execute('UPDATE machine SET machine_status = 1 WHERE id = ?', [id || 1]);
    } else if (action === 'pause') {
      await pool.execute('UPDATE machine SET machine_status = 0 WHERE id = ?', [id || 1]);
    } else if (action === 'stop') {
      await pool.execute('UPDATE machine SET machine_status = 0, current_workpiece = "", processing_duration = 0 WHERE id = ?', [id || 1]);
    }

    res.json({ success: true, message: `已发送${action === 'start' ? '启动' : action === 'pause' ? '暂停' : '停止'}命令`, action });
  } catch (err) {
    console.error('[/api/machine]', err);
    res.status(500).json({ success: false, message: '操作失败' });
  }
});

// ==================== 料架接口 ====================

app.get('/api/shelves', async (req, res) => {
  try {
    const [knifeRows] = await pool.execute('SELECT id, rfid, posstatus, workstate FROM knife_rack');
    const toolShelves = knifeRows.map(row => {
      let status = 'no-material';
      if (row.posstatus === 1) {
        if (row.workstate === 0) status = 'pending';
        else if (row.workstate === 1) status = 'processing';
        else if (row.workstate === 2) status = 'completed';
      }
      return { id: row.id, position: row.id, toolNo: row.rfid, status, activated: false, selected: false };
    });

    const [steelRows] = await pool.execute(
      'SELECT id, rfid, posstatus, scanstatus, workstate, priority FROM rack_administrate'
    );
    const steelShelves = steelRows.map(row => {
      let status = 'no-material';
      if (row.posstatus === 1) {
        if (row.scanstatus === 0) {
          status = 'to-scan';
        } else if (row.scanstatus === 1) {
          if (row.workstate === 0) {
            status = row.priority === 1 ? 'urgent' : 'pending';
          } else if (row.workstate === 1) {
            status = 'processing';
          } else if (row.workstate === 2) {
            status = 'completed';
          }
        } else if (row.scanstatus === 2) {
          status = 'scan-failed';
        }
      }
      return { id: row.id, position: row.id, monitorId: row.rfid, status, activated: false, selected: false };
    });

    res.json({ toolShelves, steelShelves });
  } catch (err) {
    console.error('[/api/shelves]', err);
    res.status(500).json({ error: 'Database error' });
  }
});

// ==================== 分包程序接口 ====================

// POST /api/sub/log - 接收分包程序日志
app.post('/api/sub/log', (req, res) => {
  const { step, displayName, level, content } = req.body || {};
  console.log(`[${displayName || step}] ${level}: ${content}`);

  // 同时推送给前端
  broadcastLog(level, displayName || step, content, { step });

  res.json({ success: true });
});

// POST /api/rack/update - 接收料架更新通知（来自分包程序）
app.post('/api/rack/update', async (req, res) => {
  try {
    const { position, rackType, status, rfid, message } = req.body || {};

    console.log(`[料架更新] 位置:${position}, 类型:${rackType}, 状态:${status}, RFID:${rfid}`);

    // 广播给所有前端客户端
    broadcastToFrontend({
      type: 'rack-update',
      data: {
        position,
        rackType,  // 'tool' 或 'steel'
        status,    // no-material, to-scan, pending, urgent, processing, completed, scan-failed
        rfid,
        message,
        timestamp: new Date().toISOString()
      }
    });

    res.json({ success: true, message: '更新已推送' });
  } catch (err) {
    console.error('[/api/rack/update]', err);
    res.status(500).json({ success: false, message: '更新失败' });
  }
});

// POST /api/rack/refresh - 触发前端刷新整个料架
app.post('/api/rack/refresh', async (req, res) => {
  try {
    // 获取最新料架数据
    const [knifeRows] = await pool.execute('SELECT id, rfid, posstatus, workstate FROM knife_rack');
    const toolShelves = knifeRows.map(row => {
      let status = 'no-material';
      if (row.posstatus === 1) {
        if (row.workstate === 0) status = 'pending';
        else if (row.workstate === 1) status = 'processing';
        else if (row.workstate === 2) status = 'completed';
      }
      return { id: row.id, position: row.id, toolNo: row.rfid, status };
    });

    const [steelRows] = await pool.execute(
      'SELECT id, rfid, posstatus, scanstatus, workstate, priority FROM rack_administrate'
    );
    const steelShelves = steelRows.map(row => {
      let status = 'no-material';
      if (row.posstatus === 1) {
        if (row.scanstatus === 0) {
          status = 'to-scan';
        } else if (row.scanstatus === 1) {
          if (row.workstate === 0) {
            status = row.priority === 1 ? 'urgent' : 'pending';
          } else if (row.workstate === 1) {
            status = 'processing';
          } else if (row.workstate === 2) {
            status = 'completed';
          }
        } else if (row.scanstatus === 2) {
          status = 'scan-failed';
        }
      }
      return { id: row.id, position: row.id, monitorId: row.rfid, status };
    });

    // 广播完整刷新
    broadcastToFrontend({
      type: 'rack-refresh',
      data: { toolShelves, steelShelves }
    });

    res.json({ success: true, message: '刷新已推送' });
  } catch (err) {
    console.error('[/api/rack/refresh]', err);
    res.status(500).json({ success: false, message: '刷新失败' });
  }
});

// ==================== 启动 ====================

const PORT = process.env.PORT || 3000;
server.listen(PORT, () => {
  console.log(`Server running on http://localhost:${PORT}`);
  console.log(`WebSocket server running on ws://localhost:${PORT}`);
});
