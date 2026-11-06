namespace MachineMonitor
{
    public static class WorkflowController
    {
        public static string GetStatus() { return WorkflowService.GetStatus(); }

        /// <summary>编排页拉取完整步骤配置（含停用项、模式、仿真开关）</summary>
        public static string GetSteps() { return WorkflowService.GetStatus(); }

        /// <summary>保存编排：整表覆盖，前端把拖拽后的完整数组发回来</summary>
        public static string Save(string raw) { return WorkflowService.SaveFlow(raw); }

        /// <summary>编排计划：流程中 + 池中两段（amis 页用）</summary>
        public static string GetPlan() { return WorkflowService.GetPlan(); }

        /// <summary>保存编排计划（提交的视为在流程中，其余降级回池子）</summary>
        public static string SavePlan(string raw) { return WorkflowService.SavePlan(raw, false); }

        /// <summary>保存并立即启动</summary>
        public static string SavePlanAndStart(string raw) { return WorkflowService.SavePlan(raw, true); }

        public static string Start(string raw) { return WorkflowService.Start(raw); }
        public static string Stop() { return WorkflowService.Stop(); }
        public static string Pause() { return WorkflowService.Pause(); }
        public static string Resume() { return WorkflowService.Resume(); }
    }
}
