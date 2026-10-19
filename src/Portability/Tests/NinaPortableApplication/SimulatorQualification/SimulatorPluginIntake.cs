using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace NINA.Plugin {
    internal static class SimulatorPluginIntake {
        internal static IReadOnlyList<string> VerifyAndGetAssemblies() {
            var directory = Path.Combine(AppContext.BaseDirectory, "QualificationPlugins");
            var expected = new Dictionary<string, string>(StringComparer.Ordinal) {
                ["LICENSE.txt"] = "CA2ABDF695884C77EA4B4A5B64CA7B732D9D9DBADE4EEBC1C2E76C53E9E3BC83",
                ["OfficialTemplateFixture.dll"] = "2F0808BFE1042537740AA09EF3969324A61895FA766F4D54A1040C0DA2E86283",
                ["OfficialTemplateFixture.pdb"] = "5A076EB178145FCBEEA756FEEFF244E744755F59DE12EFA2CD58D30B7D680297",
                ["plugin-manifest.json"] = "8561797EC9E5BEE5407063FE258484CA9F7AC90860C28559826F6F7955C4DD9E",
            };
            if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The owned pinned qualification plugin folder is required.");
            var files = Directory.GetFiles(directory);
            if (Directory.GetDirectories(directory).Length != 0 || files.Length != expected.Count)
                throw new InvalidOperationException("Unexpected qualification plugin closure members.");
            foreach (var file in files) {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0
                        || !expected.TryGetValue(Path.GetFileName(file), out var hash)
                        || !StringComparer.Ordinal.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))), hash))
                    throw new InvalidOperationException("The qualification plugin closure differs from its pinned official-template-derived fixture.");
            }
            return new[] { Path.Combine(directory, "OfficialTemplateFixture.dll") };
        }
    }
}
