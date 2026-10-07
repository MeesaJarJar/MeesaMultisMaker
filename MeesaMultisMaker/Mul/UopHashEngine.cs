namespace MeesaMultisMaker.Mul
{
    /// <summary>
    /// Shared Jenkins hash function (HashLittle2) used by UO UOP files for entry lookups.
    /// </summary>
    internal static class UopHashEngine
    {
        /// <summary>
        /// Jenkins hash function (HashLittle2) used by UO for file lookups
        /// </summary>
        public static ulong HashLittle2(string s)
        {
            uint a, b, c;
            int length = s.Length;

            a = b = c = 0xDEADBEEF + (uint)length;

            int i = 0;

            while (length > 12)
            {
                a += (uint)(s[i] + (s[i + 1] << 8) + (s[i + 2] << 16) + (s[i + 3] << 24));
                b += (uint)(s[i + 4] + (s[i + 5] << 8) + (s[i + 6] << 16) + (s[i + 7] << 24));
                c += (uint)(s[i + 8] + (s[i + 9] << 8) + (s[i + 10] << 16) + (s[i + 11] << 24));

                a -= c; a ^= (c << 4) | (c >> 28); c += b;
                b -= a; b ^= (a << 6) | (a >> 26); a += c;
                c -= b; c ^= (b << 8) | (b >> 24); b += a;
                a -= c; a ^= (c << 16) | (c >> 16); c += b;
                b -= a; b ^= (a << 19) | (a >> 13); a += c;
                c -= b; c ^= (b << 4) | (b >> 28); b += a;

                length -= 12;
                i += 12;
            }

            if (length > 0)
            {
                switch (length)
                {
                    case 12: c += (uint)(s[i + 11] << 24); goto case 11;
                    case 11: c += (uint)(s[i + 10] << 16); goto case 10;
                    case 10: c += (uint)(s[i + 9] << 8); goto case 9;
                    case 9: c += s[i + 8]; goto case 8;
                    case 8: b += (uint)(s[i + 7] << 24); goto case 7;
                    case 7: b += (uint)(s[i + 6] << 16); goto case 6;
                    case 6: b += (uint)(s[i + 5] << 8); goto case 5;
                    case 5: b += s[i + 4]; goto case 4;
                    case 4: a += (uint)(s[i + 3] << 24); goto case 3;
                    case 3: a += (uint)(s[i + 2] << 16); goto case 2;
                    case 2: a += (uint)(s[i + 1] << 8); goto case 1;
                    case 1: a += s[i]; break;
                }

                c ^= b; c -= (b << 14) | (b >> 18);
                a ^= c; a -= (c << 11) | (c >> 21);
                b ^= a; b -= (a << 25) | (a >> 7);
                c ^= b; c -= (b << 16) | (b >> 16);
                a ^= c; a -= (c << 4) | (c >> 28);
                b ^= a; b -= (a << 14) | (a >> 18);
                c ^= b; c -= (b << 24) | (b >> 8);
            }

            return ((ulong)b << 32) | c;
        }
    }
}
