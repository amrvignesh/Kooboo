using System.Globalization;

namespace Kooboo.Lib.Domain
{
    /// <summary>
    /// Shared helper for Internationalized Domain Name (IDN) conversion.
    /// GetAscii/GetUnicode on IdnMapping are stateless and thread-safe, so a single
    /// shared instance is used instead of allocating one per call.
    /// </summary>
    public static class IdnHelper
    {
        private static readonly IdnMapping _idn = new IdnMapping();

        /// <summary>
        /// Convert a domain to its Punycode/ASCII form. ASCII domains pass through
        /// unchanged; invalid input is returned as-is instead of throwing.
        /// </summary>
        public static string GetAscii(string domain)
        {
            if (string.IsNullOrEmpty(domain))
            {
                return domain;
            }
            try
            {
                return _idn.GetAscii(domain);
            }
            catch
            {
                return domain;
            }
        }

        /// <summary>
        /// Convert a Punycode domain to its Unicode display form. Non-Punycode and
        /// invalid input is returned as-is instead of throwing.
        /// </summary>
        public static string GetUnicode(string domain)
        {
            if (string.IsNullOrEmpty(domain))
            {
                return domain;
            }
            try
            {
                return _idn.GetUnicode(domain);
            }
            catch
            {
                return domain;
            }
        }
    }
}
