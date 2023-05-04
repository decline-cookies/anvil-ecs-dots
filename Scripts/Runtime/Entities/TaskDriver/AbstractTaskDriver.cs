using Anvil.CSharp.Collections;
using Anvil.CSharp.Core;
using Anvil.CSharp.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using Debug = UnityEngine.Debug;

namespace Anvil.Unity.DOTS.Entities.TaskDriver
{
    /// <summary>
    /// Given a "Task" to complete, the TaskDriver handles ensuring it is populated, processed and completed by
    /// defining the data needed, any subtasks to accomplish and the Unity Jobs to do the work required.
    /// TaskDrivers are contextual, meaning that the work they accomplish is unique to their usage in different parts
    /// of an application or as different sub task drivers as part of larger, more complex Task Drivers.
    /// The goal of a TaskDriver is to convert the specific contextual data into general agnostic data that the corresponding
    /// <see cref="AbstractTaskDriverSystem"/> will process in parallel. The results of that system processing
    /// are then picked up by the TaskDriver to be converted to specific contextual data again and passed on to
    /// a sub task driver or to another system.
    /// </summary>
    public abstract class AbstractTaskDriver : AbstractAnvilBase, ITaskSetOwner
    {
        private static readonly Type TASK_DRIVER_SYSTEM_TYPE = typeof(TaskDriverSystem<>);
        private static readonly Type COMPONENT_SYSTEM_GROUP_TYPE = typeof(ComponentSystemGroup);


        private readonly PersistentDataSystem m_PersistentDataSystem;
        private readonly List<AbstractTaskDriver> m_SubTaskDrivers;
        private readonly string m_UniqueContextIdentifier;

        private TaskSetOwnerID m_WorldUniqueID;
        private bool m_IsHardened;
        private bool m_HasCancellableData;

        /// <summary>
        /// Reference to the associated <see cref="World"/>
        /// </summary>
        public World World { get; }

        internal AbstractTaskDriver Parent { get; private set; }

        internal AbstractTaskDriverSystem TaskDriverSystem { get; }

        internal TaskSet TaskSet { get; }

        /// <summary>
        /// Data Stream representing requests to Cancel an <see cref="Entity"/>
        /// </summary>
        public IDriverCancelRequestDataStream CancelRequestDataStream
        {
            get => TaskSet.CancelRequestsDataStream;
        }

        /// <summary>
        /// Data Stream representing when Cancel Requests are Complete
        /// </summary>
        public IDriverDataStream<CancelComplete> CancelCompleteDataStream
        {
            get => TaskSet.CancelCompleteDataStream;
        }

        internal TaskSetOwnerID WorldUniqueID
        {
            get
            {
                //Make sure we're only calling this after we've generated the ID
                Debug.Assert(m_WorldUniqueID.IsValid);
                return m_WorldUniqueID;
            }
        }

        AbstractTaskDriverSystem ITaskSetOwner.TaskDriverSystem
        {
            get => TaskDriverSystem;
        }

        TaskSet ITaskSetOwner.TaskSet
        {
            get => TaskSet;
        }

        TaskSetOwnerID ITaskSetOwner.WorldUniqueID
        {
            get => WorldUniqueID;
        }

        List<AbstractTaskDriver> ITaskSetOwner.SubTaskDrivers
        {
            get => m_SubTaskDrivers;
        }

        bool ITaskSetOwner.HasCancellableData
        {
            get
            {
                Debug_EnsureHardened();
                return m_HasCancellableData;
            }
        }

        protected ITaskDriverSystem System
        {
            get => new ContextTaskDriverSystemWrapper(TaskDriverSystem, this);
        }

        /// <summary>
        /// Creates a new instance of a <see cref="AbstractTaskDriver"/>
        /// </summary>
        /// <param name="world">The <see cref="World"/> this Task Driver is a part of.</param>
        /// <param name="uniqueContextIdentifier">
        /// An optional unique identifier to identify this TaskDriver by. This is necessary when there are two or more of the
        /// same type of TaskDrivers at the same level in the hierarchy.
        /// Ex.
        /// ShootTaskDriver
        ///  - TimerTaskDriver (for time between shots)
        ///  - TimerTaskDriver (for reloading)
        /// 
        /// Both TimerTaskDriver's would conflict as being siblings of the ShootTaskDriver so they would need a unique
        /// context identifier to distinguish them for ensuring migration happens properly between worlds and data
        /// goes to the correct location.
        /// </param>
        protected AbstractTaskDriver(World world, string uniqueContextIdentifier = null)
        {
            m_UniqueContextIdentifier = uniqueContextIdentifier ?? string.Empty;
            World = world;
            TaskDriverManagementSystem taskDriverManagementSystem = World.GetOrCreateSystem<TaskDriverManagementSystem>();
            m_PersistentDataSystem = World.GetOrCreateSystem<PersistentDataSystem>();

            m_SubTaskDrivers = new List<AbstractTaskDriver>();
            TaskSet = new TaskSet(this);

            Type taskDriverType = GetType();
            Type taskDriverSystemType = TASK_DRIVER_SYSTEM_TYPE.MakeGenericType(taskDriverType);

            //If this isn't the first TaskDriver of this type, then the System will have been created for this World.
            TaskDriverSystem = (AbstractTaskDriverSystem)World.GetExistingSystem(taskDriverSystemType);
            //If not, then we will want to explicitly create it and ensure it is part of the lifecycle.
            if (TaskDriverSystem == null)
            {
                TaskDriverSystem = (AbstractTaskDriverSystem)Activator.CreateInstance(taskDriverSystemType, World);
                World.AddSystem(TaskDriverSystem);
                ComponentSystemGroup systemGroup = GetSystemGroup();
                systemGroup.AddSystemToUpdateList(TaskDriverSystem);
            }

            TaskDriverSystem.RegisterTaskDriver(this);
            taskDriverManagementSystem.RegisterTaskDriver(this);
        }

        protected override void DisposeSelf()
        {
            //We own our sub task drivers so dispose them
            m_SubTaskDrivers.DisposeAllAndTryClear();

            TaskSet.Dispose();

            base.DisposeSelf();
        }

        public override string ToString()
        {
            return $"{GetType().GetReadableName()}|{WorldUniqueID}|{m_UniqueContextIdentifier}";
        }

        private ComponentSystemGroup GetSystemGroup()
        {
            Type systemGroupType = GetSystemGroupType();
            if (!COMPONENT_SYSTEM_GROUP_TYPE.IsAssignableFrom(systemGroupType))
            {
                throw new InvalidOperationException($"Tried to get the {COMPONENT_SYSTEM_GROUP_TYPE.GetReadableName()} for {this} but {systemGroupType.GetReadableName()} is not a valid group type!");
            }

            return (ComponentSystemGroup)World.GetOrCreateSystem(systemGroupType);
        }

        private Type GetSystemGroupType()
        {
            Type type = GetType();
            UpdateInGroupAttribute updateInGroupAttribute = type.GetCustomAttribute<UpdateInGroupAttribute>();
            return updateInGroupAttribute == null ? typeof(SimulationSystemGroup) : updateInGroupAttribute.GroupType;
        }

        //*************************************************************************************************************
        // CONFIGURATION
        //*************************************************************************************************************

        protected TTaskDriver AddSubTaskDriver<TTaskDriver>(TTaskDriver subTaskDriver)
            where TTaskDriver : AbstractTaskDriver
        {
            Debug.Assert(subTaskDriver.Parent == null);
            subTaskDriver.Parent = this;
            m_SubTaskDrivers.Add(subTaskDriver);

            return subTaskDriver;
        }

        protected IDriverDataStream<TInstance> CreateDataStream<TInstance>(CancelRequestBehaviour cancelRequestBehaviour = CancelRequestBehaviour.Delete, string uniqueContextIdentifier = null)
            where TInstance : unmanaged, IEntityProxyInstance
        {
            IDriverDataStream<TInstance> dataStream = TaskSet.CreateDataStream<TInstance>(cancelRequestBehaviour, uniqueContextIdentifier ?? string.Empty);

            return dataStream;
        }

        protected IDriverEntityPersistentData<T> CreateEntityPersistentData<T>()
            where T : unmanaged, IEntityPersistentDataInstance
        {
            EntityPersistentData<T> entityPersistentData = TaskSet.CreateEntityPersistentData<T>();
            return entityPersistentData;
        }

        protected IWorldEntityPersistentData<T> GetOrCreateWorldEntityPersistentData<T>()
            where T : unmanaged, IEntityPersistentDataInstance
        {
            EntityPersistentData<T> entityPersistentData = m_PersistentDataSystem.GetOrCreateEntityPersistentData<T>();
            return entityPersistentData;
        }

        protected IThreadPersistentData<T> GetOrCreateThreadPersistentData<T>()
            where T : unmanaged, IThreadPersistentDataInstance
        {
            ThreadPersistentData<T> threadPersistentData = m_PersistentDataSystem.GetOrCreateThreadPersistentData<T>();
            return threadPersistentData;
        }

        //*************************************************************************************************************
        // JOB CONFIGURATION - DRIVER LEVEL
        //*************************************************************************************************************

        /// <summary>
        /// Configures a Job that is triggered by instances being present in the passed in <see cref="IDriverDataStream{TInstance}"/>
        /// </summary>
        /// <param name="dataStream">The <see cref="IDriverDataStream{TInstance}"/> to trigger the job off of.</param>
        /// <param name="scheduleJobFunction">The scheduling function to call to schedule the job.</param>
        /// <param name="batchStrategy">The <see cref="BatchStrategy"/> to use for executing the job.</param>
        /// <typeparam name="TInstance">The type of instance contained in the <see cref="IDriverDataStream{TInstance}"/></typeparam>
        /// <returns>A <see cref="IJobConfig"/> to allow for chaining more configuration options.</returns>
        protected IJobConfig ConfigureJobTriggeredBy<TInstance>(
            IDriverDataStream<TInstance> dataStream,
            JobConfigScheduleDelegates.ScheduleDataStreamJobDelegate<TInstance> scheduleJobFunction,
            BatchStrategy batchStrategy)
            where TInstance : unmanaged, IEntityProxyInstance
        {
            return TaskSet.ConfigureJobTriggeredBy(
                (EntityProxyDataStream<TInstance>)dataStream,
                scheduleJobFunction,
                batchStrategy);
        }

        /// <summary>
        /// Configures a Job that is triggered by <see cref="Entity"/> or <see cref="IComponentData"/> being
        /// present in the passed in <see cref="EntityQuery"/>
        /// </summary>
        /// <param name="entityQuery">The <see cref="EntityQuery"/> to trigger the job off of.</param>
        /// <param name="scheduleJobFunction">The scheduling function to call to schedule the job.</param>
        /// <param name="batchStrategy">The <see cref="BatchStrategy"/> to use for executing the job.</param>
        /// <returns>A <see cref="IJobConfig"/> to allow for chaining more configuration options.</returns>
        protected IJobConfig ConfigureJobTriggeredBy(
            EntityQuery entityQuery,
            JobConfigScheduleDelegates.ScheduleEntityQueryJobDelegate scheduleJobFunction,
            BatchStrategy batchStrategy)
        {
            return TaskSet.ConfigureJobTriggeredBy(
                entityQuery,
                scheduleJobFunction,
                batchStrategy);
        }

        //TODO: #73 - Implement other job types

        //*************************************************************************************************************
        // HARDENING
        //*************************************************************************************************************

        internal void GenerateWorldUniqueID(Dictionary<TaskSetOwnerID, ITaskSetOwner> taskSetOwnersByUniqueID)
        {
            //If we have a parent, we include their id in ours, otherwise we're top level.
            string idPath = $"{(Parent != null ? Parent.WorldUniqueID : string.Empty)}/{GetType().AssemblyQualifiedName}{m_UniqueContextIdentifier}";
            m_WorldUniqueID = new TaskSetOwnerID(idPath.GetBurstHashCode32());
            taskSetOwnersByUniqueID.Add(m_WorldUniqueID, this);

            foreach (AbstractTaskDriver subTaskDriver in m_SubTaskDrivers)
            {
                subTaskDriver.GenerateWorldUniqueID(taskSetOwnersByUniqueID);
            }

            TaskDriverSystem.GenerateWorldUniqueID(taskSetOwnersByUniqueID);
        }
        
        internal void Harden()
        {
            Debug_EnsureNotHardened();
            m_IsHardened = true;

            //Drill down so that the lowest Task Driver gets hardened
            foreach (AbstractTaskDriver subTaskDriver in m_SubTaskDrivers)
            {
                subTaskDriver.Harden();
            }

            //Harden our TaskDriverSystem if it hasn't been already
            TaskDriverSystem.Harden();

            //Harden our own TaskSet
            TaskSet.Harden();

            //TODO: #138 - Can we consolidate this into the TaskSet and have TaskSets aware of parenting instead
            m_HasCancellableData = TaskSet.ExplicitCancellationCount > 0
                || TaskDriverSystem.HasCancellableData
                || m_SubTaskDrivers.Any(subtaskDriver => subtaskDriver.m_HasCancellableData);
        }

        internal void AddJobConfigsTo(List<AbstractJobConfig> jobConfigs)
        {
            TaskSet.AddJobConfigsTo(jobConfigs);
        }

        void ITaskSetOwner.AddResolvableDataStreamsTo(Type type, List<AbstractDataStream> dataStreams)
        {
            TaskSet.AddResolvableDataStreamsTo(type, dataStreams);
        }

        //*************************************************************************************************************
        // MIGRATION
        //*************************************************************************************************************

        internal void AddToMigrationLookup(
            string parentPath,
            Dictionary<string, TaskSetOwnerID> migrationTaskSetOwnerIDLookup,
            Dictionary<string, DataTargetID> migrationDataTargetIDLookup,
            PersistentDataSystem persistentDataSystem)
        {
            //Construct the unique path for this TaskDriver. By default, out unique migration suffix is empty but if we
            //conflict with another, then we'll need to get the user to provide one.
            string typeName = GetType().AssemblyQualifiedName;
            string path = $"{parentPath}{typeName}{m_UniqueContextIdentifier}-";
            Debug_EnsureNoDuplicateMigrationData(path, migrationTaskSetOwnerIDLookup);
            migrationTaskSetOwnerIDLookup.Add(path, m_WorldUniqueID);

            //Get our TaskSet to populate all the possible DataTargetIDs
            TaskSet.AddToMigrationLookup(path, migrationDataTargetIDLookup, persistentDataSystem);

            //Try and do the same for our system (there can only be one), will gracefully fail if we have already done this
            string systemPath = $"{typeName}-System";
            if (migrationTaskSetOwnerIDLookup.TryAdd(systemPath, TaskDriverSystem.WorldUniqueID))
            {
                TaskDriverSystem.TaskSet.AddToMigrationLookup(systemPath, migrationDataTargetIDLookup, persistentDataSystem);
            }

            //Then recurse downward to catch all the sub task drivers
            foreach (AbstractTaskDriver subTaskDriver in m_SubTaskDrivers)
            {
                subTaskDriver.AddToMigrationLookup(path, migrationTaskSetOwnerIDLookup, migrationDataTargetIDLookup, persistentDataSystem);
            }
        }

        //*************************************************************************************************************
        // SAFETY
        //*************************************************************************************************************

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void Debug_EnsureNoDuplicateMigrationData(string path, Dictionary<string, TaskSetOwnerID> migrationTaskSetOwnerIDLookup)
        {
            if (migrationTaskSetOwnerIDLookup.ContainsKey(path))
            {
                throw new InvalidOperationException($"TaskDriver {this} at path {path} already exists. There are two or more of the same task driver at the same level. They will require a unique migration suffix to be set in their constructor.");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void Debug_EnsureNotHardened()
        {
            if (m_IsHardened)
            {
                throw new InvalidOperationException($"Trying to Harden {this} but {nameof(Harden)} has already been called!");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void Debug_EnsureHardened()
        {
            if (!m_IsHardened)
            {
                throw new InvalidOperationException($"Expected {this} to be Hardened but it hasn't yet!");
            }
        }
    }
}
