using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Portals.Lab.Validation
{
    // Раннее подключение логов необходимо до Awake остальных компонентов проверочной сцены.
    [DefaultExecutionOrder(-32000)]
    public sealed class PortalCheckRun : MonoBehaviour
    {
        [SerializeField] private PortalCheckIdentity identity;
        private PortalCheckSession session;
        private double startedAt;
        private int capturedFrames;
        private int crossings;
        private bool quitting;
        private bool writeArtifacts;
        private int deferredExit = -1;
        public const double WatchdogSeconds = 180;

        public static PortalCheckRun Current { get; private set; }
        public string OutputDirectory => identity.outputDirectory;
        public bool IsCompleted => session != null && session.Completed;

        public void SetBuildIdentity(PortalCheckIdentity embedded) => identity = embedded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCurrent() => Current = null;

        private void Awake()
        {
            // Даже при загрузке проверочного ассета обычный Editor не выдаёт сертифицированный результат.
            if (Application.isEditor || identity == null || string.IsNullOrEmpty(identity.check))
            {
                enabled = false;
                return;
            }
            if (Current != null)
            {
                Current.RecordFailure("Duplicate embedded run context.");
                enabled = false;
                return;
            }
            Current = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            startedAt = Time.realtimeSinceStartupAsDouble;
            session = new PortalCheckSession(identity);
            Application.logMessageReceived += OnLog;
            Application.wantsToQuit += OnWantsToQuit;
            var expected = new Dictionary<string, string>();
            foreach (string key in new[] { "PORTAL_CHECK_NAME", "PORTAL_CHECK_COMMIT", "PORTAL_CHECK_PROJECT",
                "PORTAL_CHECK_RUN_ID", "PORTAL_CHECK_OUTPUT" })
            {
                string value = Environment.GetEnvironmentVariable(key);
                if (value != null) expected[key] = value;
            }
            string invalid = identity.ValidateExpected(expected);
            if (invalid.Length != 0)
            {
                // Чужой runId не должен перезаписывать артефакты прежнего запуска.
                Finish(identity.check, "Failed", 0, 0, invalid, true);
                return;
            }
            writeArtifacts = true;
            try { Directory.CreateDirectory(OutputDirectory); }
            catch (Exception)
            {
                Finish(identity.check, "Failed", 0, 0, "Cannot create output directory.", true);
                return;
            }
            Debug.Log("[PortalCheckMetadata] " + JsonUtility.ToJson(Metadata()));
        }

        public void RecordFailure(string reason) => session?.RecordFailure(reason);

        public void RecordProgress(int frameCount, int crossingCount)
        {
            capturedFrames = frameCount;
            crossings = crossingCount;
        }

        public void Complete(string check, string status, int frameCount, int crossingCount, string reason)
        {
            Finish(check, status, frameCount, crossingCount, reason, true);
        }

        private void Update()
        {
            if (deferredExit >= 0)
            {
                int exit = deferredExit;
                deferredExit = -1;
                Application.Quit(exit);
            }
            if (session != null && !session.Completed && Time.realtimeSinceStartupAsDouble - startedAt >= WatchdogSeconds)
                Finish(identity.check, "Blocked", capturedFrames, crossings, "Realtime watchdog expired.", true);
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                RecordFailure("Unity reported " + type + "; see player.log.");
        }

        private bool OnWantsToQuit()
        {
            if (deferredExit >= 0) return false;
            if (session == null || session.Completed) return true;
            // Guard нужен и для повторного запроса из callback итогового лога.
            // Только Update снимает его перед контролируемым ненулевым выходом.
            deferredExit = 2;
            Finish(identity.check, "Blocked", capturedFrames, crossings, "Application quit before explicit completion.", false);
            return false;
        }

        private void OnApplicationQuit()
        {
            quitting = true;
            if (session != null && !session.Completed)
                Finish(identity.check, "Blocked", capturedFrames, crossings, "Application quit before explicit completion.", false);
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
            Application.wantsToQuit -= OnWantsToQuit;
            if (session != null && !session.Completed)
                Finish(identity.check, "Blocked", capturedFrames, crossings, "Run context disabled before completion.", !quitting);
            if (Current == this) Current = null;
        }

        private void Finish(string check, string status, int frames, int count, string reason, bool requestQuit)
        {
            PortalCheckResult result = session?.TryComplete(check, status, frames, count, reason);
            if (result == null) return;
            AddRuntimeMetadata(result);
            if (writeArtifacts)
            {
                try { File.WriteAllText(Path.Combine(OutputDirectory, "result.json"), JsonUtility.ToJson(result, true)); }
                catch (Exception)
                {
                    result.status = "Failed";
                    result.failureReason = "Cannot persist final result artifact.";
                }
            }
            Debug.Log("[PortalCheckResult] " + JsonUtility.ToJson(result));
            int exitCode = result.status == "Passed" ? 0 : result.status == "Failed" ? 1 : 2;
            Environment.ExitCode = exitCode;
            if (requestQuit && !quitting) Application.Quit(exitCode);
        }

        private PortalCheckResult Metadata()
        {
            var metadata = new PortalCheckResult
            {
                check = identity.check, commit = identity.commit, projectPath = identity.projectPath,
                runId = identity.runId, dirty = identity.dirty, sourceDigest = identity.sourceDigest,
                unityVersion = identity.unityVersion, hdrpVersion = identity.hdrpVersion
            };
            AddRuntimeMetadata(metadata);
            return metadata;
        }

        private static void AddRuntimeMetadata(PortalCheckResult result)
        {
            result.gpu = SystemInfo.graphicsDeviceName;
            result.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            result.width = Screen.width;
            result.height = Screen.height;
            result.vSyncCount = QualitySettings.vSyncCount;
            result.developmentBuild = Debug.isDebugBuild;
        }
    }
}
