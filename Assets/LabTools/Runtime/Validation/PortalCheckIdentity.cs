using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Portals.Lab.Validation
{
    [Serializable]
    public sealed class PortalCheckIdentity
    {
        public string check;
        public string commit;
        public string projectPath;
        public string runId;
        public string outputDirectory;
        public bool dirty;
        public string sourceDigest;
        public string unityVersion;
        public string hdrpVersion;

        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(check) || !Regex.IsMatch(check, "^[A-Za-z][A-Za-z0-9]*$"))
                return "Invalid embedded check name.";
            if (commit == null || !Regex.IsMatch(commit, "^([0-9a-f]{40}|[0-9a-f]{64})$"))
                return "Embedded full Git SHA is missing.";
            if (!AbsoluteDirectory(projectPath) || !AbsoluteDirectory(outputDirectory))
                return "Embedded project/output path must be absolute and canonical.";
            if (string.IsNullOrWhiteSpace(runId)) return "Embedded run ID is missing.";
            if (sourceDigest == null || !Regex.IsMatch(sourceDigest, "^[0-9a-f]{64}$"))
                return "Embedded source digest is missing.";
            if (string.IsNullOrWhiteSpace(unityVersion) || string.IsNullOrWhiteSpace(hdrpVersion))
                return "Embedded Unity/HDRP version is missing.";
            return string.Empty;
        }

        public string ValidateExpected(IDictionary<string, string> expected)
        {
            string invalid = Validate();
            if (invalid.Length != 0) return invalid;
            var embedded = new Dictionary<string, string>
            {
                { "PORTAL_CHECK_NAME", check }, { "PORTAL_CHECK_COMMIT", commit },
                { "PORTAL_CHECK_PROJECT", projectPath }, { "PORTAL_CHECK_RUN_ID", runId },
                { "PORTAL_CHECK_OUTPUT", outputDirectory }
            };
            foreach (var pair in embedded)
            {
                if (!expected.TryGetValue(pair.Key, out string value)) continue;
                bool path = pair.Key == "PORTAL_CHECK_PROJECT" || pair.Key == "PORTAL_CHECK_OUTPUT";
                bool equal = path ? SameDirectory(value, pair.Value) : value == pair.Value;
                if (!equal) return "Runtime expectation differs from embedded " + pair.Key + ".";
            }
            return string.Empty;
        }

        public static string CanonicalDirectory(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        public static bool SameDirectory(string a, string b)
        {
            try
            {
                return Path.IsPathFullyQualified(a) && Path.IsPathFullyQualified(b)
                    && string.Equals(CanonicalDirectory(a), CanonicalDirectory(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        private static bool AbsoluteDirectory(string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
                    && CanonicalDirectory(path).Length > Path.GetPathRoot(path).Length
                    && string.Equals(path.Replace('/', '\\').TrimEnd('\\'),
                        CanonicalDirectory(path).Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }
    }
}
