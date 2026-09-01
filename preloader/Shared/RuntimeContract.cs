using System;
using System.Linq;
using System.Reflection;

namespace FixWorld.RuntimeBridge
{
    internal sealed class RuntimeContract
    {
        internal const string AssemblyName = "FixWorld.Runtime";
        internal const string TypeName = "FixWorld.Runtime.FixWorldRuntime";
        internal const int Version = 1;

        private readonly MethodInfo attachMod;
        private readonly MethodInfo shutdown;
        private readonly MethodInfo startEarly;

        private RuntimeContract(Type entrypoint)
        {
            FieldInfo version = entrypoint.GetField(
                "ContractVersion",
                BindingFlags.Public | BindingFlags.Static);
            if (version == null ||
                version.FieldType != typeof(int) ||
                !version.IsLiteral ||
                (int)version.GetRawConstantValue() != Version)
            {
                throw new NotSupportedException(
                    "FixWorld.Runtime contract version is missing or incompatible.");
            }

            startEarly = RequireMethod(entrypoint, "StartEarly", Type.EmptyTypes);
            attachMod = RequireMethod(
                entrypoint,
                "AttachMod",
                new[] { typeof(object), typeof(string), typeof(float) });
            shutdown = RequireMethod(
                entrypoint,
                "Shutdown",
                Type.EmptyTypes);
        }

        internal static RuntimeContract Bind(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            if (!string.Equals(
                    assembly.GetName().Name,
                    AssemblyName,
                    StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    "Expected " + AssemblyName + ", got " +
                    assembly.GetName().Name + ".");
            }

            Type entrypoint = assembly.GetType(TypeName, throwOnError: true);
            return new RuntimeContract(entrypoint);
        }

        internal static RuntimeContract BindLoaded()
        {
            Assembly assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .SingleOrDefault(candidate => string.Equals(
                    candidate.GetName().Name,
                    AssemblyName,
                    StringComparison.Ordinal));
            if (assembly == null)
            {
                throw new InvalidOperationException(
                    "FixWorld.Runtime was not loaded by the early loader.");
            }

            return Bind(assembly);
        }

        internal void StartEarly()
        {
            Invoke(startEarly, null);
        }

        internal void AttachMod(
            object mod,
            string modRoot,
            float ddsCacheMaxGiB)
        {
            Invoke(attachMod, new object[] { mod, modRoot, ddsCacheMaxGiB });
        }

        internal void Shutdown()
        {
            Invoke(shutdown, null);
        }

        private static MethodInfo RequireMethod(
            Type entrypoint,
            string name,
            Type[] parameters)
        {
            MethodInfo method = entrypoint.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: parameters,
                modifiers: null);
            if (method == null || method.ReturnType != typeof(void))
            {
                throw new MissingMethodException(entrypoint.FullName, name);
            }

            return method;
        }

        private static void Invoke(MethodInfo method, object[] arguments)
        {
            try
            {
                method.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException != null)
            {
                throw exception.InnerException;
            }
        }
    }
}
