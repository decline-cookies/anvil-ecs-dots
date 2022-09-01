using Anvil.Unity.DOTS.Data;
using Anvil.Unity.DOTS.Jobs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Jobs;

namespace Anvil.Unity.DOTS.Entities
{
    public abstract partial class AbstractTaskDriverSystem<TKey, TTaskData> : AbstractTaskDriverSystem
        where TKey : unmanaged, IEquatable<TKey>
        where TTaskData : unmanaged, IKeyedData<TKey>, ITaskData
    {
        public new VirtualData<TKey, TTaskData> TaskData
        {
            get => base.TaskData as VirtualData<TKey, TTaskData>;
        }

        protected AbstractTaskDriverSystem()
        {
            base.TaskData = CreateTaskData<TKey, TTaskData>();
        }
    }
    
    /// <summary>
    /// A type of System that runs <see cref="AbstractTaskDriver"/>s during its update phase.
    /// </summary>
    public abstract partial class AbstractTaskDriverSystem : AbstractAnvilSystemBase
    {
        private readonly List<AbstractTaskDriver> m_TaskDrivers;
        //TODO: Do we need to have it be a lookup or will we only ever have one?
        private readonly VirtualDataLookup m_TaskDataLookup;
        private readonly List<JobTaskWorkConfig> m_UpdateJobData;
        
        public AbstractVirtualData TaskData
        {
            get;
            protected set;
        }
        
        protected AbstractTaskDriverSystem()
        {
            m_TaskDataLookup = new VirtualDataLookup();
            m_TaskDrivers = new List<AbstractTaskDriver>();
            m_UpdateJobData = new List<JobTaskWorkConfig>();
        }
        
        protected override void OnDestroy()
        {
            m_TaskDataLookup.Dispose();
            
            m_UpdateJobData.Clear();
            
            //Note: We don't dispose TaskDrivers here because their parent or direct reference will do so. 
            m_TaskDrivers.Clear();

            base.OnDestroy();
        }

        protected JobTaskWorkConfig ConfigureUpdateJob(JobTaskWorkConfig.ScheduleJobDelegate scheduleJobDelegate)
        {
            JobTaskWorkConfig config = new JobTaskWorkConfig(scheduleJobDelegate, this);
            m_UpdateJobData.Add(config);
            return config;
        }
        
        //TODO: #39 - Some way to remove the update Job

        protected VirtualData<TKey, TInstance> CreateTaskData<TKey, TInstance>()
            where TKey : unmanaged, IEquatable<TKey>
            where TInstance : unmanaged, IKeyedData<TKey>
        {
            VirtualData<TKey, TInstance> virtualData = VirtualData<TKey, TInstance>.Create();
            m_TaskDataLookup.AddData(virtualData);
            return virtualData;
        }

        internal void RegisterTaskDriver(AbstractTaskDriver taskDriver)
        {
            Debug_EnsureTaskDriverSystemRelationship(taskDriver);
            m_TaskDrivers.Add(taskDriver);
        }
        
        protected override void OnUpdate()
        {
            Dependency = UpdateTaskDriverSystem(Dependency);
        }

        private JobHandle UpdateTaskDriverSystem(JobHandle dependsOn)
        {
            //Have drivers be given the chance to add to the Instance Data
            dependsOn = m_TaskDrivers.BulkScheduleParallel(dependsOn, AbstractTaskDriver.POPULATE_SCHEDULE_DELEGATE);
            
            //Consolidate our instance data to operate on it
            dependsOn = m_TaskDataLookup.ConsolidateForFrame(dependsOn);
            
            //TODO: #38 - Allow for cancels to occur
            
            //Allow the generic work to happen in the derived class
            dependsOn = m_UpdateJobData.BulkScheduleParallel(dependsOn, JobTaskWorkConfig.PREPARE_AND_SCHEDULE_SCHEDULE_DELEGATE);
            
            //Have drivers consolidate their result data
            dependsOn = m_TaskDrivers.BulkScheduleParallel(dependsOn, AbstractTaskDriver.CONSOLIDATE_SCHEDULE_DELEGATE);
            
            //TODO: #38 - Allow for cancels on the drivers to occur
            
            //Have drivers to do their own generic work
            dependsOn = m_TaskDrivers.BulkScheduleParallel(dependsOn, AbstractTaskDriver.UPDATE_SCHEDULE_DELEGATE);

            //Ensure this system's dependency is written back
            return dependsOn;
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void Debug_EnsureTaskDriverSystemRelationship(AbstractTaskDriver taskDriver)
        {
            if (taskDriver.System != this)
            {
                throw new InvalidOperationException($"{taskDriver} is part of system {taskDriver.System} but it should be {this}!");
            }

            if (m_TaskDrivers.Contains(taskDriver))
            {
                throw new InvalidOperationException($"Trying to add {taskDriver} to {this}'s list of Task Drivers but it is already there!");
            }
        }
    }
}
