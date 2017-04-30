using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using DNSProtocol;
using NLog;

namespace DNSResolver
{
    /**
     * A clock is responsible for reporting the current time, in seconds.
     *
     * The only responsibility is that this should be monotonic - one second
     * that the system is running should be reported as one second here, without
     * anything like timezones or leap seconds interfering.
     */
    public interface IClock
    {
        long Time { get; }
    }

    /**
     * An IClock implementation based upon the Stopwatch.
     */
    public class StopwatchClock: IClock
    {
        private Stopwatch stopwatch;

        public StopwatchClock()
        {
            stopwatch = new Stopwatch();
            stopwatch.Start();
        }

        public long Time
        {
            get { return stopwatch.ElapsedMilliseconds / 1000; }
        }
    }

    /**
     * A priority queue implemented via binary heap.
     */
    public class PriorityQueue<T> where T: IEquatable<T>
    {
        private class QueueEntry
        {
            public long Priority;
            public T Value;
        }

        private List<QueueEntry> items;

        public PriorityQueue()
        {
            items = new List<QueueEntry>();
            items.Add(null);
        }

        // true when there is nothing in the queue, or false otherwise
        public bool Empty
        {
            get { return items.Count == 1; }
        }

        // The number of elements currently in the queue
        public int Count
        {
            get { return items.Count - 1; }
        }

        // The value currently at the top of the queue
        public T Top
        {
            get
            {
                if (Empty)
                {
                    throw new InvalidOperationException("Empty PriorityQueue has no top element");
                }
                return items[1].Value;
            }
        }

        // The priority of the value currently at the top of the queue
        public long TopPriority
        {
            get
            {
                if (Empty)
                {
                    throw new InvalidOperationException("Empty PriorityQueue has no top element");
                }
                return items[1].Priority;
            }
        }

        /**
         * Adds a new element to the queue with the given priority
         */
        public void Push(T value, long priority)
        {
            var entry = new QueueEntry();
            entry.Value = value;
            entry.Priority = priority;
            items.Add(entry);

            HeapifyUp(items.Count - 1);
        }


        /**
         * Removes the top item from the queue and returns it
         */
        public T Pop()
        {
            if (Empty)
            {
                throw new InvalidOperationException("Cannot pop from an empty priority queue");
            }

            T old_top_value = Top;
            var new_top = items[items.Count - 1];
            items[1] = new_top;
            items.RemoveAt(items.Count - 1);

            HeapifyDown(1);
            return old_top_value;
        }

        /**
         * Updates the priority on the given item in the queue
         */
        public void Reprioritize(T value, long priority)
        {
            var index = -1;
            for (var i = 1; i < items.Count; i++)
            {
                if (items[i].Value.Equals(value))
                {
                    index = i;
                    break;
                }
            }

            if (index == -1)
            {
                throw new KeyNotFoundException("Cannot find " + value + " in priority queue");
            }

            var old_priority = items[index].Priority;
            items[index].Priority = priority;
            if (priority > old_priority)
            {
                HeapifyDown(index);
            }
            else
            {
                HeapifyUp(index);
            }
        }

        /**
         * Maintains the heap property by recursively swapping values up the heap.
         */
        private void HeapifyUp(int index)
        {
            var parent = index / 2;
            if (parent < 1 || items[parent].Priority <= items[index].Priority)
            {
                return;
            }

            var tmp = items[parent];
            items[parent] = items[index];
            items[index] = tmp;

            HeapifyUp(parent);
        }

        /**
         * Maintains the heap property by recursively swapping values down the heap.
         */
        private void HeapifyDown(int index)
        {
            var left = 2 * index;
            var right = left + 1;

            int min;
            if (left < items.Count && right < items.Count)
            {
                if (items[left].Priority < items[right].Priority)
                {
                    min = left;
                }
                else
                {
                    min = right;
                }
            }
            else if (left < items.Count)
            {
                min = left;
            }
            else
            {
                // This is a child-less node, which can't be swapped down
                return;
            }

            if (items[index].Priority > items[min].Priority)
            {
                var tmp = items[index];
                items[index] = items[min];
                items[min] = tmp;
                HeapifyDown(min);
            }
        }
    }

    /**
     * A general interface for DNS caches - the only requirements are that you
     * should be able to add things to them and query them, subject to the
     * requirement that the TTLs of any resources are not out of date.
     */
    public interface IDNSCache
    {
        void Add(DNSRecord record);
        HashSet<DNSRecord> Query(Domain domain, AddressClass cls, ResourceRecordType type);
    }

    /**
     * A cache which does nothing. Useful for the CLI, since it only handles
     * single requests.
     */
    public class NoopCache: IDNSCache
    {
        public void Add(DNSRecord record)
        {
        }

        public HashSet<DNSRecord> Query(Domain domain, AddressClass cls, ResourceRecordType type)
        {
            return new HashSet<DNSRecord>();
        }
    }

    /**
     * The DNS cache is used to store records that have to do with a
     * particular Domain and resource type.
     */
    public class ResolverCache : IDNSCache
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private HashSet<DNSRecord> complete_cache;
        private PriorityQueue<DNSRecord> ttl_queue;
        private IClock clock;
        private int max_size;

        public ResolverCache(IClock clock, int max_size)
        {
            complete_cache = new HashSet<DNSRecord>();
            ttl_queue = new PriorityQueue<DNSRecord>();
            this.clock = clock;
            this.max_size = max_size;
        }

        /**
         * Adds a new record into the cache.
         */
        public void Add(DNSRecord record)
        {
            // This is effectively useless - if we don't know how to parse it,
            // then we can't get anything relevant from it
            if (record.Resource == null)
            {
                logger.Trace("Ignoring unusable record {0}", record);
                return;
            }

            complete_cache.Add(record);
            ttl_queue.Push(record, clock.Time + record.TimeToLive);

            // If adding this evicted something, then get rid of it
            if (complete_cache.Count > max_size)
            {
                CleanAmount(complete_cache.Count - max_size);
            }

            logger.Trace("Added {0} to cache", record);
        }

        /**
         * Gets the records from the cache that match the given domain, class
         * and type. For class and type, UNSUPPORTED are considered wildcards.
         */
        public HashSet<DNSRecord> Query(Domain domain, AddressClass cls, ResourceRecordType type)
        {
            var check_class = cls != AddressClass.UNSUPPORTED;
            var check_type = type != ResourceRecordType.UNSUPPORTED;
            var output = new HashSet<DNSRecord>();

            logger.Trace("Cache asked for domain {0}, class {1}, rtype {2}", domain, cls, type);

            // Avoid going over any records which are out of date
            CleanTTL();

            foreach (var record in complete_cache)
            {
                if (record.Name != domain ||
                    (check_class && record.AddressClass != cls) ||
                    (check_type && record.Resource.Type != type))
                {
                    continue;
                }

                output.Add(record);
            }

            logger.Trace("Found {0} records", output.Count);
            return output;
        }

        /**
         * Removes any elements that have expired their TTL.
         */
        private void CleanTTL()
        {
            long now = clock.Time;

            while (!ttl_queue.Empty && ttl_queue.TopPriority <= now)
            {
                var to_remove = ttl_queue.Pop();
                logger.Trace("Cleaning record {0} at {1}", to_remove, now);

                complete_cache.Remove(to_remove);
            }
        }

        /**
         * Removes N elements from the queue, starting with those that are set
         * to expire closest to now.
         */
        private void CleanAmount(int n)
        {
            while (n > 0 && !ttl_queue.Empty)
            {
                var to_remove = ttl_queue.Pop();
                logger.Trace("Cleaning record {0} from overfull cache", to_remove);

                complete_cache.Remove(to_remove);
                n--;
            }
        }
    }
}
