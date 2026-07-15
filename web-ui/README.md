# 管理界面示例

这是一个静态示例页面，包含：

- 左上角显示当前日期和时间（每秒更新）。
- 右上角显示用户名和网络在线/离线状态（使用 `navigator.onLine`）。
- 左侧可折叠的菜单栏，菜单项：`主界面`、`任务管理`、`料架管理`、`日志查询`。

## 料架管理功能

料架管理界面有两个分页：刀具料架和钢件料架。

- **刀具料架**：料位图标为带柄圆柱形，标签显示"料位编号-T刀号"。
- **钢件料架**：料位图标为方块形，标签显示"料位编号-监控编号"。

每个分页包含：
- 料位管理框：按钮用于激活、锁定、全选、禁用、无料等操作（钢件有急件按钮）。
- 料位状态提示框：显示各种状态图标和颜色（加工中蓝色、待加工黄色等）。

料位图标根据状态显示不同颜色，单选框在激活后显示，可用于批量操作。

## 后端API

后端使用Node.js + Express + MySQL2连接数据库。

- 数据库：qianduantest (MySQL, 端口3306, 用户root, 密码123456)
- 数据表：
  - knife_rack：刀具料架 (id=料位编号, rfid=T刀号, posstatus, workstate)
  - rack_administrate：钢件料架 (id=料位编号, rfid=监控编号, posstatus, scanstatus, workstate, priority)

状态逻辑：
- 刀具：posstatus=0 → 无料; posstatus=1 → 根据workstate (0=待加工,1=加工中,2=加工完成)
- 钢件：posstatus=0 → 无料; posstatus=1 & scanstatus=0 → 待扫描; posstatus=1 & scanstatus=1 → 根据workstate (0=待加工/急件,1=加工中,2=加工完成)

配置文件：config.json (可修改数据库连接参数)

启动后端：`npm start` (端口3000)

快速使用：

1. 启动后端：`npm start`
2. 打开 `web-ui/index.html`（直接在浏览器中打开即可）。
3. 可点击左上角的三线按钮收起/展开侧边栏。
4. 如需修改用户名，打开浏览器控制台运行：

```js
localStorage.setItem('demo_username','张三')
location.reload()
```

文件说明：

- `index.html`：页面主文件。
- `styles.css`：样式文件。
- `script.js`：前端交互逻辑。
- `server.js`：后端API服务器。
- `config.json`：数据库配置。
- `package.json`：Node.js依赖。

如需我把界面改成响应式更紧凑或加入图标/真实登录，请告诉我。