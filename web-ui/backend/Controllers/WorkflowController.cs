namespace MachineMonitor
{
    public static class WorkflowController
    {
        public static string GetStatus() { return WorkflowService.GetStatus(); }
        public static string Start(string raw) { return WorkflowService.Start(raw); }
        public static string Stop() { return WorkflowService.Stop(); }
        public static string Pause() { return WorkflowService.Pause(); }
        public static string Resume() { return WorkflowService.Resume(); }
    }
}
