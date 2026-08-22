using System;
using System.Threading;

namespace Audex.Audio
{
    /// <summary>
    /// Coordinates process-wide BASS lifetime operations. Decode-only background jobs may run
    /// concurrently, but Bass.Init/Bass.Free must never race an active native decoder.
    /// </summary>
    internal static class BassLifetimeCoordinator
    {
        private static readonly ReaderWriterLockSlim Gate =
            new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

        public static IDisposable EnterBackgroundWork()
        {
            Gate.EnterReadLock();
            return new ReadLease();
        }

        public static T RunExclusive<T>(Func<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            Gate.EnterWriteLock();
            try
            {
                return action();
            }
            finally
            {
                Gate.ExitWriteLock();
            }
        }

        public static void RunExclusive(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            Gate.EnterWriteLock();
            try
            {
                action();
            }
            finally
            {
                Gate.ExitWriteLock();
            }
        }

        private sealed class ReadLease : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Gate.ExitReadLock();
            }
        }
    }
}
