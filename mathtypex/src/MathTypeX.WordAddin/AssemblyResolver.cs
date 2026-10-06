using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MathTypeX.WordAddin
{
    /// <summary>
    /// Word nạp add-in với thư mục gốc là thư mục của WINWORD.EXE, nên assembly đi kèm (MathTypeX.Interop.Contracts…)
    /// có thể không được tìm thấy. Handler này tìm chúng trong thư mục của add-in.
    /// </summary>
    internal static class AssemblyResolver
    {
        private static bool _installed;

        public static string AddinDirectory => Path.GetDirectoryName(new Uri(typeof(AssemblyResolver).Assembly.CodeBase).LocalPath) ?? "";

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                string name = new AssemblyName(args.Name).Name ?? "";
                var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
                if (loaded is not null) return loaded;
                string path = Path.Combine(AddinDirectory, name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
        }
    }
}
