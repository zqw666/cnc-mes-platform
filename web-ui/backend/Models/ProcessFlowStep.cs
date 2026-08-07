namespace MachineMonitor
{
    public class ProcessFlowStep
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public int Status { get; set; }
        public int Priority { get; set; }
    }

    public class WorkFlowStep : ProcessFlowStep
    {
    }
}
