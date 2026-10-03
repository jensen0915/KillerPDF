using System;
using System.IO;

namespace KillerPDF.Services
{
    internal static class AtomicFile
    {
        // Stage next to the destination so replacement never crosses volumes.
        public static void Copy(string source, string destination)
        {
            string target = Path.GetFullPath(destination);
            string staged = Path.Combine(Path.GetDirectoryName(target)!, ".killerpdf-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(source, staged);
                if (File.Exists(target)) File.Replace(staged, target, null);
                else File.Move(staged, target);
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }
    }
}
