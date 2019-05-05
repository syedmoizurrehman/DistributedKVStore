using System;

namespace AppUtilities
{
    /// <summary>
    /// Contains application properties which are used across multiple nodes.
    /// </summary>
    public static class Properties
    {
        public static int PortNumber => 8080;

        /// <summary>
        /// Defines how many number of nodes will store each key.
        /// </summary>
        public static int ReplicationFactor { get; set; }

        /// <summary>
        /// Gets or sets the number of nodes used for actual data storage.
        /// </summary>
        public static int RingSize { get; set; }

        /// <summary>
        /// Initializes default values.
        /// </summary>
        static Properties()
        {
            RingSize = 3;
            ReplicationFactor = 2;
        }
    }


    /// <summary>
    /// Used for generating thead-safe random numbers. Copied from https://stackoverflow.com/a/1262619
    /// </summary>
    public static class ThreadSafeRandom
    {
        [ThreadStatic] private static Random Local;

        public static Random CurrentThreadsRandom => Local ?? (Local = new Random(unchecked(Environment.TickCount * 31 + Environment.CurrentManagedThreadId)));
    }
}
