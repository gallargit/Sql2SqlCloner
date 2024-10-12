using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace SqlServerTypes
{
    /// <summary>
    /// Utility methods related to CLR Types for SQL Server
    /// </summary>
    internal static class Utilities
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string libname);

        /// <summary>
        /// Loads the required native assemblies for the current architecture (x86 or x64)
        /// </summary>
        /// <param name="rootApplicationPath">
        /// Root path of the current application. Use Server.MapPath(".") for ASP.NET applications
        /// and AppDomain.CurrentDomain.BaseDirectory for desktop applications.
        /// </param>
        public static void LoadNativeAssemblies(string rootApplicationPath)
        {
            const string spatialDll = "SqlServerSpatial160.dll";
            var architecture = IntPtr.Size > 4 ? "x64" : "x86";
            var SNIDll = $"Microsoft.Data.SqlClient.SNI.{architecture}.dll";

            var spatialAssemblyPath = Path.Combine(rootApplicationPath, architecture);
            var spatialAssemblyName = Path.Combine(spatialAssemblyPath, spatialDll);
            var SNIAssemblyName = Path.Combine(rootApplicationPath, SNIDll);

            //After the application is built, the "SqlServerSpatial160.dll" file should be copied to
            //the "bin\x64" folder, but it fails sometimes the first time, it looks like it's an error with MSBuild
            //the way to fix it is: build the solution, then rebuild it. Since this is inconvenient and easy
            //to forget a quick workaround is used here: copy the Dll file from the "packages" folder at runtime
            //The same happens with the "Microsoft.Data.SqlClient.SNI.x64.dll" file, which should be copied to the "bin" folder
            if (Debugger.IsAttached)
            {
                //copy Sql2SqlCloner\packages\Microsoft.SqlServer.Types.160.1000.6\runtimes\win-x64\native\SqlServerSpatial160.dll
                //to   Sql2SqlCloner\bin\Debug\x64
                if (!File.Exists(spatialAssemblyName))
                {
                    var packageBinaryPath = Path.Combine(rootApplicationPath, "..", "..", "packages");
                    //look for latest "Microsoft.SqlServer.Types*" folder
                    var typesDllDirectories = Directory.GetDirectories(packageBinaryPath).Where(d => d.Contains("Microsoft.SqlServer.Types"));
                    if (typesDllDirectories.Any())
                    {
                        var typeDllDirectory = typesDllDirectories.OrderBy(d => d).Last();
                        packageBinaryPath = Path.Combine(packageBinaryPath, typeDllDirectory, "runtimes", $"win-{architecture}", "native", spatialDll);
                        if (File.Exists(packageBinaryPath))
                        {
                            if (!Directory.Exists(spatialAssemblyPath))
                            {
                                Directory.CreateDirectory(spatialAssemblyPath);
                            }
                            File.Copy(packageBinaryPath, spatialAssemblyName, false);
                        }
                    }
                }

                //copy Sql2SqlCloner\packages\Microsoft.Data.SqlClient.SNI.5.0.1\build\net46\Microsoft.Data.SqlClient.SNI.x64.dll
                //to   Sql2SqlCloner\bin\Debug
                if (!File.Exists(SNIAssemblyName))
                {
                    var packageBinaryPath = Path.Combine(spatialAssemblyPath, "..", "..", "..", "packages");
                    //look for latest "Microsoft.Data.SqlClient*" folder
                    var sqlClientDirectories = Directory.GetDirectories(packageBinaryPath).Where(d => d.Contains("Microsoft.Data.SqlClient"));
                    if (sqlClientDirectories.Any())
                    {
                        var sqlClientDirectory = sqlClientDirectories.OrderBy(d => d).Last();
                        packageBinaryPath = Path.Combine(packageBinaryPath, sqlClientDirectory, "build");
                        //this could be "net46" or "net462" in the newer versions
                        packageBinaryPath = Directory.GetDirectories(packageBinaryPath).Where(d => d.Contains("net4")).OrderBy(d => d).Last();
                        packageBinaryPath = Path.Combine(packageBinaryPath, SNIDll);
                        if (File.Exists(packageBinaryPath))
                        {
                            File.Copy(packageBinaryPath, SNIAssemblyName, false);
                        }
                    }
                }
            }
            LoadNativeAssembly(spatialAssemblyName);
        }

        private static void LoadNativeAssembly(string fullAssemblyName)
        {
            if (LoadLibrary(fullAssemblyName) == IntPtr.Zero)
            {
                throw new Exception(string.Format("Error loading {0} (ErrorCode: {1})", fullAssemblyName, Marshal.GetLastWin32Error()));
            }
        }
    }
}