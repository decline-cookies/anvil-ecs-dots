using Anvil.CSharp.Logging;
using Unity.Collections;

namespace Anvil.Unity.DOTS.Logging
{
    /// <summary>
    /// A collection of DOTS specific extension methods for <see cref="Log.Logger"/>.
    /// </summary>
    public static class LoggerExtension
    {
        /// <summary>
        /// Creates an instance of <see cref="BurstableLogger{PrefixStringType}"/> from the
        /// <see cref="Log.Logger" /> instance.
        /// </summary>
        /// <param name="logger">The <see cref="Log.Logger" /> to copy configuration from.</param>
        /// <param name="appendToMessagePrefix">
        /// A string to append to the <see cref="Log.Logger" />'s existing prefix.
        /// (max length: <see cref="FixedString32Bytes.Capacity"/>)
        /// </param>
        /// <returns>A <see cref="BurstableLogger{FixedString32Bytes}" /> instance.</returns>
        public static BurstableLogger<FixedString32Bytes> AsBurstable(this in Log.Logger logger, string appendToMessagePrefix = null)
        {
            return AsBurstable<FixedString32Bytes>(logger, appendToMessagePrefix);
        }

        /// <summary>
        /// Creates an instance of <see cref="BurstableLogger{PrefixStringType}"/> from the
        /// <see cref="Log.Logger" /> instance.
        /// </summary>
        /// <typeparam name="PrefixStringType"></typeparam>
        /// <param name="logger">The <see cref="Log.Logger" /> to copy configuration from.</param>
        /// <param name="appendToMessagePrefix">
        /// A string to append to the <see cref="Log.Logger" />'s existing prefix.
        /// (max length: <see cref="PrefixStringType.Capacity"/>)
        /// </param>
        /// <returns>A <see cref="BurstableLogger{PrefixStringType}" /> instance.</returns>
        public static BurstableLogger<PrefixStringType> AsBurstable<PrefixStringType>(this in Log.Logger logger, string appendToMessagePrefix = null) where PrefixStringType : struct, INativeList<byte>, IUTF8Bytes
        {
            return new BurstableLogger<PrefixStringType>(logger, appendToMessagePrefix);
        }
    }
}
