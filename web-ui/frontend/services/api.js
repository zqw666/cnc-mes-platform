const API_BASE = 'http://localhost:8888';

function apiGet(path) {
  return fetch(API_BASE + path, { method: 'GET' }).then(r => r.json());
}

function apiPost(path, data) {
  return fetch(API_BASE + path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data)
  }).then(r => r.json());
}

const ApiService = {
  // Dashboard
  getDashboard() { return apiGet('/api/dashboard'); },

  // Config
  getConfig() { return apiGet('/api/config'); },
  getConfigRaw() { return apiGet('/api/config/raw'); },
  saveConfig(data) { return apiPost('/api/config', data); },

  // Workflow
  getWorkflowStatus() { return apiGet('/api/workflow/status'); },
  startWorkflow(raw) { return apiPost('/api/workflow/start', raw); },
  stopWorkflow() { return apiPost('/api/workflow/stop'); },
  pauseWorkflow() { return apiPost('/api/workflow/pause'); },
  resumeWorkflow() { return apiPost('/api/workflow/resume'); },

  // Logs
  getLogs(raw) { return apiPost('/api/logs', raw); },

  // Machine
  controlMachine(raw) { return apiPost('/api/machine/control', raw); },
  createMachine(raw) { return apiPost('/api/machine/create', raw); },

  // Rack
  getRackStatus() { return apiGet('/api/rack/status'); },
  updateRackStatus(raw) { return apiPost('/api/rack/status', raw); },
  createRack(raw) { return apiPost('/api/rack/create', raw); },

  // Sub log
  sendSubLog(raw) { return apiPost('/api/sub/log', raw); }
};
