using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoggerLibrary
{
    public class ObservableConcurrentQueue<T> : IEnumerable<T>
    {
        private readonly ConcurrentQueue<T> queue = new ConcurrentQueue<T>();

        public event EventHandler<QueueChangedEventArgs<T>> QueueChanged;

        public int Count => queue.Count;

        public ObservableConcurrentQueue()
        {
        }

        public ObservableConcurrentQueue(ObservableConcurrentQueue<T> other)
        {
            foreach (var item in other)
            {
                queue.Enqueue(item);
            }
        }

        public void Enqueue(T item)
        {
            queue.Enqueue(item);
            OnQueueChanged(new QueueChangedEventArgs<T>(item, QueueChangedAction.Enqueue));
        }

        public bool TryDequeue(out T result)
        {
            if (queue.TryDequeue(out result))
            {
                OnQueueChanged(new QueueChangedEventArgs<T>(result, QueueChangedAction.Dequeue));
                return true;
            }
            return false;
        }

        public Task ClearAsync()
        {
            queue.Clear();
            return Task.CompletedTask;
        }

        public T GetFirstItem()
        {
            if (queue.TryPeek(out var queueItem))
            {
                return queueItem;
            }
            return default;
        }

        public T GetLastItem()
        {
            T lastItem = default;
            foreach (var item in queue)
            {
                lastItem = item;
            }
            return lastItem;
        }

        protected virtual void OnQueueChanged(QueueChangedEventArgs<T> e)
        {
            QueueChanged?.Invoke(this, e);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return queue.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return queue.GetEnumerator();
        }
    }

    public enum QueueChangedAction
    {
        Enqueue,
        Dequeue
    }

    public class QueueChangedEventArgs<T> : EventArgs
    {
        public T Item { get; }
        public QueueChangedAction Action { get; }

        public QueueChangedEventArgs(T item, QueueChangedAction action)
        {
            Item = item;
            Action = action;
        }
    }


}
