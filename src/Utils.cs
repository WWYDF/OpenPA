using Random = System.Random;

namespace OpenUtils
{
    class OPAUtils
    {
        private static Random random = new Random();

        public static string RandomString(int len)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Range(0, len)
                .Select(_ => chars[random.Next(chars.Length)]).ToArray());
        }
    }
}
