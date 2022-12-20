using Anvil.CSharp.Core;
using Anvil.Unity.DOTS.Jobs;
using System.Reflection;
using Unity.Jobs;

namespace Anvil.Unity.DOTS.Entities.Tasks
{
    internal interface IDataSource : IAnvilDisposable
    {
        public static readonly BulkScheduleDelegate<IDataSource> CONSOLIDATE_SCHEDULE_FUNCTION = BulkSchedulingUtil.CreateSchedulingDelegate<IDataSource>(nameof(Consolidate), BindingFlags.Instance | BindingFlags.Public);
        public void Harden();

        public JobHandle Consolidate(JobHandle dependsOn);
    }
}
