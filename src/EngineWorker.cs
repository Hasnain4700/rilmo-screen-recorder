using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Rilmo
{
    // All engine operations run in order on one dedicated thread. Waiting is blocking,
    // not polling: this worker consumes no capture resources while the app is idle.
    internal sealed class EngineWorker : IDisposable
    {
        private readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        private readonly Thread thread;
        public EngineWorker()
        {
            thread = new Thread(() => { foreach (var operation in queue.GetConsumingEnumerable()) operation(); }) { IsBackground = true, Name = "Rilmo recording engine" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }
        public Task<T> Run<T>(Func<T> operation)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Add(() => { try { completion.SetResult(operation()); } catch (Exception ex) { completion.SetException(ex); } });
            return completion.Task;
        }
        public Task Run(Action operation) { return Run(() => { operation(); return true; }); }
        public void Dispose() { queue.CompleteAdding(); }
    }
}
