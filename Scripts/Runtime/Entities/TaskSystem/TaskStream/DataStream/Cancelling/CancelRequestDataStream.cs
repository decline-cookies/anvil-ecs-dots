using Anvil.Unity.DOTS.Data;
using Anvil.Unity.DOTS.Jobs;
using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Anvil.Unity.DOTS.Entities.Tasks
{
    internal class CancelRequestDataStream : AbstractEntityInstanceIDDataStream
    {
        private static readonly int MAX_ELEMENTS_PER_CHUNK = ChunkUtil.MaxElementsPerChunk<EntityProxyInstanceID>();
        
        private readonly AbstractCancelFlow m_CancelFlow;
        internal UnsafeParallelHashMap<EntityProxyInstanceID, byte> Lookup { get; }

        public CancelRequestDataStream(AbstractCancelFlow cancelFlow)
        {
            m_CancelFlow = cancelFlow;
            Lookup = new UnsafeParallelHashMap<EntityProxyInstanceID, byte>(MAX_ELEMENTS_PER_CHUNK, Allocator.Persistent);
        }

        protected override void DisposeDataStream()
        {
            Lookup.Dispose();
            base.DisposeDataStream();
        }

        //*************************************************************************************************************
        // SERIALIZATION
        //*************************************************************************************************************

        //TODO: #83 - Add support for Serialization. Hopefully from the outside or via extension methods instead of functions
        //here but keeping the TODO for future reminder.

        //*************************************************************************************************************
        // CONSOLIDATION
        //*************************************************************************************************************

        protected sealed override JobHandle ConsolidateForFrame(JobHandle dependsOn)
        {
            dependsOn = JobHandle.CombineDependencies(dependsOn,
                                                      AccessController.AcquireAsync(AccessType.ExclusiveWrite),
                                                      m_CancelFlow.AcquireProgressLookup(AccessType.ExclusiveWrite, out UnsafeParallelHashMap<EntityProxyInstanceID, bool> progressLookup));

            ConsolidateCancelRequestsJob consolidateCancelRequestsJob = new ConsolidateCancelRequestsJob(Pending,
                                                                                                         Lookup,
                                                                                                         progressLookup);
            dependsOn = consolidateCancelRequestsJob.Schedule(dependsOn);

            m_CancelFlow.ReleaseProgressLookup(dependsOn);
            AccessController.ReleaseAsync(dependsOn);
            return dependsOn;
        }

        //*************************************************************************************************************
        // JOBS
        //*************************************************************************************************************

        [BurstCompile]
        private struct ConsolidateCancelRequestsJob : IJob
        {
            [ReadOnly] private UnsafeTypedStream<EntityProxyInstanceID> m_Pending;
            private UnsafeParallelHashMap<EntityProxyInstanceID, byte> m_Lookup;
            private UnsafeParallelHashMap<EntityProxyInstanceID, bool> m_ProgressLookup;

            public ConsolidateCancelRequestsJob(UnsafeTypedStream<EntityProxyInstanceID> pending,
                                                UnsafeParallelHashMap<EntityProxyInstanceID, byte> lookup,
                                                UnsafeParallelHashMap<EntityProxyInstanceID, bool> progressLookup)
            {
                m_Pending = pending;
                m_Lookup = lookup;
                m_ProgressLookup = progressLookup;
            }

            public void Execute()
            {
                m_Lookup.Clear();
                foreach (EntityProxyInstanceID proxyInstanceID in m_Pending)
                {
                    Debug_EnsureNoDuplicates(proxyInstanceID);
                    m_Lookup.TryAdd(proxyInstanceID, 1);
                    //We have something that wants to cancel, so we assume that it will get processed this frame.
                    //If nothing processes it, it will auto-complete the next frame. 
                    m_ProgressLookup.TryAdd(proxyInstanceID, true);
                }

                m_Pending.Clear();
            }

            //*************************************************************************************************************
            // SAFETY
            //*************************************************************************************************************

            [Conditional("ANVIL_DEBUG_SAFETY_EXPENSIVE")]
            private void Debug_EnsureNoDuplicates(EntityProxyInstanceID id)
            {
                if (m_Lookup.ContainsKey(id))
                {
                    throw new InvalidOperationException($"Trying to add id of {id} but the same id already exists in the lookup! This should never happen! Investigate.");
                }
            }
        }
    }
}
