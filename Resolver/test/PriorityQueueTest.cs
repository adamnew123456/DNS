using System;
using NUnit.Framework;

namespace DNSResolver
{
    [TestFixture]
    public class PriorityQueueTest
    {
        [Test]
        public void testEmptyQueueProperties()
        {
            var pq = new PriorityQueue<int>();
            Assert.That(pq.Count, Is.EqualTo(0));
            Assert.That(pq.Empty, Is.True);
        }

        [Test]
        public void testEmptyQueueThrows()
        {
            var pq = new PriorityQueue<int>();
            Assert.Throws<InvalidOperationException>(() =>
                {
                    int _ = pq.Top;
                });
            Assert.Throws<InvalidOperationException>(() =>
                {
                    long _ = pq.TopPriority;
                });
            Assert.Throws<InvalidOperationException>(() =>
                {
                    pq.Pop();
                });
        }

        [Test]
        public void testPushQueueProperties()
        {
            var pq = new PriorityQueue<string>();
            pq.Push("Hello, World", 1);

            Assert.That(pq.Count, Is.EqualTo(1));
            Assert.That(pq.Empty, Is.False);
            Assert.That(pq.Top, Is.EqualTo("Hello, World"));
            Assert.That(pq.TopPriority, Is.EqualTo(1));
        }

        [Test]
        public void testPushQueuePop()
        {
            var pq = new PriorityQueue<string>();
            pq.Push("Hello, World", 1);
            Assert.That(pq.Pop(), Is.EqualTo("Hello, World"));
        }

        [Test]
        public void testPushQueuePopProperties()
        {
            var pq = new PriorityQueue<string>();
            pq.Push("Hello, World", 1);
            pq.Pop();

            Assert.That(pq.Count, Is.EqualTo(0));
            Assert.That(pq.Empty, Is.True);
            // The 'throwing' tests are assumed to follow, if the queue really
            // is empty again
        }

        [Test]
        public void testSeveralPushPrioity()
        {
            var pq = new PriorityQueue<string>();
            pq.Push("Goodbye, World", 1);
            pq.Push("Hello, World", 0);

            Assert.That(pq.Pop(), Is.EqualTo("Hello, World"));
            Assert.That(pq.Pop(), Is.EqualTo("Goodbye, World"));
        }

        [Test]
        public void testSeveralPushProperties()
        {
            var pq = new PriorityQueue<string>();
            pq.Push("Goodbye, World", 1);
            pq.Push("Hello, World", 0);

            Assert.That(pq.Count, Is.EqualTo(2));
            Assert.That(pq.Empty, Is.False);
            Assert.That(pq.Top, Is.EqualTo("Hello, World"));
            Assert.That(pq.TopPriority, Is.EqualTo(0));
        }

        [Test]
        public void testReprioritize()
            {
                var pq = new PriorityQueue<string>();
                pq.Push("Hello, World", 1);
                pq.Push("Goodbye, World", 0);
                pq.Reprioritize("Goodbye, World", 2);

                Assert.That(pq.Pop(), Is.EqualTo("Hello, World"));
                Assert.That(pq.Pop(), Is.EqualTo("Goodbye, World"));
            }

        [Test]
        public void testReprioritizeProperties()
            {
                var pq = new PriorityQueue<string>();
                pq.Push("Hello, World", 1);
                pq.Push("Goodbye, World", 1);
                pq.Reprioritize("Goodbye, World", 2);

                Assert.That(pq.Count, Is.EqualTo(2));
                Assert.That(pq.Empty, Is.False);
                Assert.That(pq.Top, Is.EqualTo("Hello, World"));
                Assert.That(pq.TopPriority, Is.EqualTo(1));
            }
    }
}
