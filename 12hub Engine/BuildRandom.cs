using System;
using System.Security.Cryptography;

namespace Hub12
{
    /// <summary>
    /// One shared Random object whose internal stream can be reseeded for each build.
    /// Keeping object identity stable is important because several opcode/pass classes
    /// cache a reference to TwelveHubEngine.Random in static fields.
    /// </summary>
    public sealed class BuildRandom : Random
    {
        private readonly object _gate = new object();
        private Random _inner;

        public BuildRandom()
        {
            Reseed();
        }

        public void Reseed()
        {
            byte[] seedBytes = RandomNumberGenerator.GetBytes(32);
            int seed = BitConverter.ToInt32(seedBytes, 0)
                ^ BitConverter.ToInt32(seedBytes, 4)
                ^ BitConverter.ToInt32(seedBytes, 8)
                ^ BitConverter.ToInt32(seedBytes, 12)
                ^ BitConverter.ToInt32(seedBytes, 16)
                ^ BitConverter.ToInt32(seedBytes, 20)
                ^ BitConverter.ToInt32(seedBytes, 24)
                ^ BitConverter.ToInt32(seedBytes, 28);
            lock (_gate)
            {
                _inner = new Random(seed);
            }
            CryptographicOperations.ZeroMemory(seedBytes);
        }

        public override int Next()
        {
            lock (_gate) return _inner.Next();
        }

        public override int Next(int maxValue)
        {
            lock (_gate) return _inner.Next(maxValue);
        }

        public override int Next(int minValue, int maxValue)
        {
            lock (_gate) return _inner.Next(minValue, maxValue);
        }

        public override double NextDouble()
        {
            lock (_gate) return _inner.NextDouble();
        }

        public override void NextBytes(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            lock (_gate) _inner.NextBytes(buffer);
        }

        public override void NextBytes(Span<byte> buffer)
        {
            lock (_gate) _inner.NextBytes(buffer);
        }
    }
}
