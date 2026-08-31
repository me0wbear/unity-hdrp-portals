using System;

namespace Portals.Lab.Validation
{
    [Serializable]
    public sealed class PortalCheckResult
    {
        public string check;
        public bool completed;
        public string status;
        public string commit;
        public string projectPath;
        public string runId;
        public int frameCount;
        public int crossingCount;
        public string failureReason;
        public bool dirty;
        public string sourceDigest;
        public string unityVersion;
        public string hdrpVersion;
        public string gpu;
        public string graphicsApi;
        public int width;
        public int height;
        public int vSyncCount;
        public bool developmentBuild;
    }

    public sealed class PortalCheckSession
    {
        private readonly PortalCheckIdentity identity;
        private string pendingFailure;
        public bool Completed { get; private set; }

        public PortalCheckSession(PortalCheckIdentity identity)
        {
            this.identity = identity ?? throw new ArgumentNullException(nameof(identity));
            RecordFailure(identity.Validate());
        }

        public void RecordFailure(string reason)
        {
            if (!Completed && string.IsNullOrEmpty(pendingFailure) && !string.IsNullOrWhiteSpace(reason))
                pendingFailure = reason;
        }

        public PortalCheckResult TryComplete(string check, string status, int frameCount,
            int crossingCount, string reason)
        {
            if (Completed) return null;
            Completed = true;
            if (check != identity.check) pendingFailure = "Caller check differs from embedded identity.";
            if (frameCount < 0 || crossingCount < 0 || (status == "Passed" && frameCount == 0))
                pendingFailure = "Invalid completion counters.";
            if (status != "Passed" && status != "Failed" && status != "Blocked")
                pendingFailure = "Unknown completion status.";
            if (status == "Passed" && !string.IsNullOrEmpty(reason))
                pendingFailure = "Passed completion cannot contain a failure reason.";
            if (!string.IsNullOrEmpty(pendingFailure))
            {
                status = "Failed";
                reason = pendingFailure;
            }
            else if (status == "Passed" && identity.check != "Color" && identity.check != "Seam"
                && identity.check != "SandboxParity" && identity.check != "Performance")
            {
                status = "Blocked";
                reason = "Check has no certified acceptance policy.";
            }
            if (status != "Passed" && string.IsNullOrWhiteSpace(reason)) reason = "Check did not pass.";
            return new PortalCheckResult
            {
                check = identity.check, completed = true, status = status, commit = identity.commit,
                projectPath = identity.projectPath, runId = identity.runId,
                frameCount = Math.Max(0, frameCount), crossingCount = Math.Max(0, crossingCount),
                failureReason = status == "Passed" ? string.Empty : reason,
                dirty = identity.dirty, sourceDigest = identity.sourceDigest,
                unityVersion = identity.unityVersion, hdrpVersion = identity.hdrpVersion
            };
        }
    }
}
