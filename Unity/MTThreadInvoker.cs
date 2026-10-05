#if CP_SDK_UNITY
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CP_SDK.Unity
{
    /// <summary>
    /// Thread task system
    /// </summary>
    public class MTThreadInvoker
    {
        /// <summary>
        /// Max queue size
        /// </summary>
        const int MAX_QUEUE_SIZE = 1000;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Queue class
        /// </summary>
        private class Queue
        {
            public Action[] Data = new Action[MAX_QUEUE_SIZE];
            public int WritePos = 0;
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Run condition
        /// </summary>
        private static bool m_RunCondition = false;
        /// <summary>
        /// Update thread
        /// </summary>
        private static Thread m_UpdateThread;
        /// <summary>
        /// Queues instance
        /// </summary>
        private static Queue[] m_Queues = new Queue[2]
        {
            new Queue(),
            new Queue()
        };
        /// <summary>
        /// Have queued actions
        /// </summary>
        private static volatile bool m_Queued = false;
        /// <summary>
        /// Current front queue
        /// </summary>
        private static int m_FrontQueue = 0;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Initialize
        /// </summary>
        internal static void Initialize()
        {
            if (m_UpdateThread != null)
                return;

            m_RunCondition = true;

            m_UpdateThread = new Thread(Update);
            m_UpdateThread.IsBackground = true;
            m_UpdateThread.Start();
        }
        /// <summary>
        /// Stop
        /// </summary>
        internal static void Destroy()
        {
            if (m_UpdateThread == null)
                return;

            m_RunCondition = false;
            m_UpdateThread.Join();
            m_UpdateThread = null;

            /// Clear queues
            m_Queues = new Queue[2]
            {
                new Queue(),
                new Queue()
            };
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>Serialize background-safe work; rejection faults the task, and successful completion follows the delegate return.</summary>
        public static Task<T> EnqueueRetained<T>(Func<T> p_Work)
        {
            if (p_Work == null)
                throw new ArgumentNullException(nameof(p_Work));

            var l_Completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!TryEnqueueOnThread(() =>
            {
                try { l_Completion.TrySetResult(p_Work()); }
                catch (Exception l_Exception) { l_Completion.TrySetException(l_Exception); }
            }))
                l_Completion.TrySetException(new InvalidOperationException("Worker is unavailable or full."));
            l_Completion.Task.ContinueWith(p_Task => p_Task.Exception.Handle(p_Error => true),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return l_Completion.Task;
        }

        internal static bool TryEnqueueOnThread(Action p_Action)
        {
            if (p_Action == null)
                return false;

            lock (m_Queues)
            {
                if (!m_RunCondition)
                    return false;

                var l_Queue = m_Queues[m_FrontQueue];
                if (l_Queue.WritePos >= MAX_QUEUE_SIZE)
                    return false;

                l_Queue.Data[l_Queue.WritePos++] = p_Action;
                m_Queued = true;
                return true;
            }
        }

        /// <summary>
        /// Enqueue a new action
        /// </summary>
        /// <param name="p_Action">Action to enqueue</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnqueueOnThread(Action p_Action)
        {
            if (p_Action == null)
                return;

            lock (m_Queues)
            {
                var l_Queue = m_Queues[m_FrontQueue];
                if (l_Queue.WritePos >= MAX_QUEUE_SIZE)
                {
                    ChatPlexSDK.Logger.Error("[CP_SDK.Unity][MTThreadInvoker.EnqueueOnThread] Too many actions pushed!");
                    return;
                }

                l_Queue.Data[l_Queue.WritePos++] = p_Action;
                m_Queued = true;
            }
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Thread update
        /// </summary>
        private static void Update()
        {
            while (m_RunCondition)
            {
                if (!m_Queued)
                {
                    Thread.Sleep(10);
                    continue;
                }

                var l_QueueToHandle     = m_FrontQueue;
                var l_NextFrontQueue    = (m_FrontQueue + 1) & 1;

                lock (m_Queues)
                {
                    m_FrontQueue    = l_NextFrontQueue;
                    m_Queued        = false;
                }

                var l_Queue = m_Queues[l_QueueToHandle];
                var l_Count = l_Queue.WritePos;
                var l_I     = 0;

                do
                {
                    try
                    {
                        l_Queue.Data[l_I]();
                    }
                    catch (Exception l_Exception)
                    {
                        ChatPlexSDK.Logger.Error("[CP_SDK.Unity][MTThreadInvoker.Update] Error:");
                        ChatPlexSDK.Logger.Error(l_Exception);
                    }

                    ++l_I;
                } while (l_I < l_Count);

                if (l_I < l_Count)
                {
                    var l_ToCopy        = l_Count - l_I;
                    var l_FrontQueue    = m_Queues[m_FrontQueue];

                    lock (m_Queues)
                    {
                        Array.Copy(l_Queue.Data, l_I, l_FrontQueue.Data, l_FrontQueue.WritePos, l_ToCopy);
                        l_FrontQueue.WritePos += l_ToCopy;
                        m_Queued = true;
                    }
                }

                Array.Clear(l_Queue.Data, 0, l_Count);
                l_Queue.WritePos = 0;

                Thread.Sleep(10);
            }
        }
    }
}
#endif
