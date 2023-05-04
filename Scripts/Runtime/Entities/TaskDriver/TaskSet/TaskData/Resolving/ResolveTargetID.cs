using Anvil.Unity.DOTS.Util;
using System;
using Unity.Collections;

namespace Anvil.Unity.DOTS.Entities.TaskDriver
{
    [BurstCompatible]
    internal readonly struct ResolveTargetID : IEquatable<ResolveTargetID>
    {
        public static bool operator ==(ResolveTargetID lhs, ResolveTargetID rhs)
        {
            return lhs.TypeID == rhs.TypeID && lhs.TaskSetOwnerID == rhs.TaskSetOwnerID;
        }

        public static bool operator !=(ResolveTargetID lhs, ResolveTargetID rhs)
        {
            return !(lhs == rhs);
        }

        public readonly uint TypeID;
        public readonly TaskSetOwnerID TaskSetOwnerID;

        public ResolveTargetID(uint typeID, TaskSetOwnerID taskSetOwnerID)
        {
            TypeID = typeID;
            TaskSetOwnerID = taskSetOwnerID;
        }

        public bool Equals(ResolveTargetID other)
        {
            return this == other;
        }

        public override bool Equals(object compare)
        {
            return compare is ResolveTargetID id && Equals(id);
        }

        public override int GetHashCode()
        {
            return HashCodeUtil.GetHashCode(TaskSetOwnerID.GetHashCode(), (int)TypeID);
        }

        public override string ToString()
        {
            return $"TypeID: {TypeID} - TaskSetOwnerID: {TaskSetOwnerID}";
        }

        [BurstCompatible]
        public FixedString64Bytes ToFixedString()
        {
            return new FixedString64Bytes(ToString());
            ;
        }
    }
}
