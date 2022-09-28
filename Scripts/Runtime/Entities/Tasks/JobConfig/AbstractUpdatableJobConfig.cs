using System;
using System.Collections.Generic;

namespace Anvil.Unity.DOTS.Entities
{
    internal abstract class AbstractUpdatableJobConfig : AbstractJobConfig,
                                                         IUpdatableJobConfigRequirements
    {
        private readonly JobResolveTargetMapping m_JobResolveTargetMapping;

        private DataStreamTargetResolver m_DataStreamTargetResolver;

        protected AbstractUpdatableJobConfig(TaskFlowGraph taskFlowGraph,
                                             ITaskSystem taskSystem,
                                             ITaskDriver taskDriver) : base(taskFlowGraph, taskSystem, taskDriver)
        {
            m_JobResolveTargetMapping = new JobResolveTargetMapping();
        }

        protected override void DisposeSelf()
        {
            m_DataStreamTargetResolver.Dispose();
            base.DisposeSelf();
        }
        
        //*************************************************************************************************************
        // CONFIGURATION - REQUIRED DATA - DATA STREAM
        //*************************************************************************************************************
        
        public IUpdatableJobConfigRequirements RequireResolveTarget<TResolveTarget>(TResolveTarget resolveTarget)
            where TResolveTarget : Enum
        {
            ResolveTargetUtil.Debug_EnsureEnumValidity(resolveTarget);

            //Any data streams that have registered for this resolve target type either on the system or related task drivers will be needed.
            //When the updater runs, it doesn't know yet which resolve target a particular instance will resolve to yet until it actually resolves.
            //We need to ensure that all possible locations have write access
            TaskFlowGraph.PopulateJobResolveTargetMappingForTarget(resolveTarget, m_JobResolveTargetMapping, TaskSystem);

            if (m_JobResolveTargetMapping.Mapping.Count == 0)
            {
                return this;
            }
            
            List<ResolveTargetData> resolveTargetData = m_JobResolveTargetMapping.GetResolveTargetData(resolveTarget);
            AddAccessWrapper(new JobConfigDataID(m_JobResolveTargetMapping.DataStreamType, Usage.Resolve),
                             DataStreamAsResolveTargetAccessWrapper.Create(resolveTarget, resolveTargetData));

            return this;
        }
        
        //*************************************************************************************************************
        // HARDEN
        //*************************************************************************************************************

        protected sealed override void HardenConfig()
        {
            m_DataStreamTargetResolver = new DataStreamTargetResolver(m_JobResolveTargetMapping);
        }
        
        //*************************************************************************************************************
        // EXECUTION
        //*************************************************************************************************************
        
        internal override DataStreamTargetResolver GetDataStreamChannelResolver()
        {
            return m_DataStreamTargetResolver;
        }
    }
}
