using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace FixWorld.Bootstrap
{
    public enum InstallationAction { Install, Reinstall, Uninstall }

    [DataContract]
    public sealed class InstallationMaintenance
    {
        [DataMember] private InstallationAction action;
        [DataMember] private string gameRoot, proxySource, bootstrap, helper, expectedHash;

        internal InstallationMaintenance(InstallationAction action, string gameRoot, string proxySource,
            string bootstrap, string helper, string expectedHash)
        {
            this.action = action;
            this.gameRoot = gameRoot;
            this.proxySource = proxySource;
            this.bootstrap = bootstrap;
            this.helper = helper;
            this.expectedHash = expectedHash;
        }

        public InstallationAction Action => action;
        private Installation Target => new Installation(gameRoot, proxySource, bootstrap, helper, expectedHash);
        public void Validate() => Target.ValidateMaintenance(action);
        public void Execute() => Target.ApplyMaintenance(action);

        internal string Serialize()
        {
            using var stream = new MemoryStream();
            new DataContractJsonSerializer(typeof(InstallationMaintenance)).WriteObject(stream, this);
            return Convert.ToBase64String(stream.ToArray());
        }

        internal static InstallationMaintenance Deserialize(string value)
        {
            using var stream = new MemoryStream(Convert.FromBase64String(value), false);
            return (InstallationMaintenance)new DataContractJsonSerializer(typeof(InstallationMaintenance)).ReadObject(stream);
        }
    }
}
