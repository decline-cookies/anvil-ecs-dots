using Unity.Entities;

namespace Anvil.Unity.DOTS.Entities
{
    /// <summary>
    /// Data System (no update) for managing the world's <see cref="TaskFlowGraph"/>
    /// </summary>
    //TODO: Safer way to handle this. Discussion with Mike.
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
    public partial class TaskFlowSystem : AbstractAnvilSystemBase
    {
        internal TaskFlowGraph TaskFlowGraph
        {
            get;
        }

        public TaskFlowSystem()
        {
            TaskFlowGraph = new TaskFlowGraph();
        }

        protected override void OnUpdate()
        {
            //TODO: Probably a better way to do this via a factory type. https://github.com/decline-cookies/anvil-unity-dots/pull/59#discussion_r977823711
            TaskFlowGraph.Harden();
            Enabled = false;
        }
    }
}
