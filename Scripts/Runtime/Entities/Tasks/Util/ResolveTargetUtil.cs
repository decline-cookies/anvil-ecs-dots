using System;
using System.Diagnostics;
using Unity.Collections.LowLevel.Unsafe;

namespace Anvil.Unity.DOTS.Entities.Tasks
{
    internal static class ResolveTargetUtil
    {
        private static readonly Type BYTE_TYPE = typeof(byte);
        
        //*************************************************************************************************************
        // SAFETY
        //*************************************************************************************************************

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void Debug_EnsureEnumValidity<TResolveTarget>(TResolveTarget resolveTarget)
            where TResolveTarget : Enum
        {
            Debug_EnsureEnumIsSizedProperly(typeof(TResolveTarget));
        }
        
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void Debug_EnsureEnumValidity(object resolveTarget)
        {
            Type type = resolveTarget.GetType();
            if (!type.IsEnum)
            {
                throw new InvalidOperationException($"Resolve Target Type is {type} but needs to be a {typeof(Enum)}");
            }

            Debug_EnsureEnumIsSizedProperly(type);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void Debug_EnsureEnumIsSizedProperly(Type type)
        {
            if (Enum.GetUnderlyingType(type) != BYTE_TYPE)
            {
                throw new InvalidOperationException($"Resolve Target Enum type {type} does not have underlying type of {BYTE_TYPE}. Please change to {BYTE_TYPE}.");
            }
            
            int sizeOfType = UnsafeUtility.SizeOf(type);
            int sizeOfByte = UnsafeUtility.SizeOf<byte>();

            if (sizeOfType != sizeOfByte)
            {
                throw new InvalidOperationException($"Resolve Target Enum is of size {sizeOfType} bytes but needs to be the size of a {typeof(byte)} or {sizeOfByte} byte");
            }
        }
    }
}
