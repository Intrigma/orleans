using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// Value generator for <see cref="SqsOptions.FifoMessageDeduplicationIdGenerator"/>
    /// </summary>
    public static class MessageDeduplicationIdGenerator
    {
        private static readonly string Prefix = Guid.NewGuid().ToString("N");

        public static string Next() => Prefix + "-" + Guid.NewGuid().ToString("N");
    }
}