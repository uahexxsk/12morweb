using System;

namespace Hub12.Obfuscator.Encryption
{
    /// <summary>
    /// Computes a build-salted payload checksum for generated 12hub VMs. This is a
    /// tamper-evident sanity check, not cryptographic authentication or anti-debugging.
    /// Keep the arithmetic in the exactly representable integer range of Lua 5.1 doubles.
    /// </summary>
    public static class VMIntegrityCheck
    {
        public const long Modulus = 4294967291L;

        public static long ComputePayloadHash(byte[] payload, int initialSeed, int multiplier)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (initialSeed < 0)
                throw new ArgumentOutOfRangeException(nameof(initialSeed));
            if (multiplier < 2 || multiplier > 251)
                throw new ArgumentOutOfRangeException(nameof(multiplier));

            long hash = initialSeed;
            for (int i = 0; i < payload.Length; i++)
                hash = ((hash * multiplier) + payload[i]) % Modulus;
            return hash;
        }
    }
}
