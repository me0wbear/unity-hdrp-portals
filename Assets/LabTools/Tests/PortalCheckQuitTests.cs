using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Portals.Lab.Tests
{
    public sealed class PortalCheckQuitTests
    {
        private GameObject host;
        private Component run;
        private int previousExitCode;
        private readonly List<object> results = new List<object>();
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string ResultPrefix = "[PortalCheckResult] ";

        [SetUp]
        public void SetUp()
        {
            previousExitCode = Environment.ExitCode;
            results.Clear();
            host = new GameObject("QuitRegression");
            run = host.AddComponent(LabSerializationTests.FindType("Portals.Lab.Validation.PortalCheckRun"));
            object identity = PortalCheckPolicyTests.Identity();
            run.GetType().GetMethod("SetBuildIdentity").Invoke(run, new[] { identity });
            object session = Activator.CreateInstance(
                LabSerializationTests.FindType("Portals.Lab.Validation.PortalCheckSession"), identity);
            run.GetType().GetField("session", PrivateInstance).SetValue(run, session);
            Application.logMessageReceived += CollectResult;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= CollectResult;
            // Тест вызывает только обработчики запроса, не Update с настоящим Application.Quit.
            if (run != null) run.GetType().GetField("session", PrivateInstance).SetValue(run, null);
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            Environment.ExitCode = previousExitCode;
        }

        private object Invoke(string name) => run.GetType().GetMethod(name, PrivateInstance).Invoke(run, null);

        private void CollectResult(string message, string stack, LogType type)
        {
            if (message.StartsWith(ResultPrefix, StringComparison.Ordinal))
                results.Add(JsonUtility.FromJson(message.Substring(ResultPrefix.Length),
                    LabSerializationTests.FindType("Portals.Lab.Validation.PortalCheckResult")));
        }

        [Test]
        public void RepeatedEarlyQuitIsRejectedUntilControlledRetry()
        {
            Assert.That(Invoke("OnWantsToQuit"), Is.False);
            Assert.That(Invoke("OnWantsToQuit"), Is.False, "Completed session must not bypass the pending exit guard.");
            Assert.That(Invoke("OnWantsToQuit"), Is.False);
        }

        [Test]
        public void ReentrantQuitFromFinalLogIsRejected()
        {
            bool? allowed = null;
            Application.LogCallback reenter = (message, stack, type) =>
            {
                if (message.StartsWith(ResultPrefix, StringComparison.Ordinal))
                    allowed = (bool)Invoke("OnWantsToQuit");
            };
            Application.logMessageReceived += reenter;
            try
            {
                Assert.That(Invoke("OnWantsToQuit"), Is.False);
                Assert.That(allowed, Is.EqualTo(false), "Arm the guard before emitting the final log.");
                Assert.That(results, Has.Count.EqualTo(1));
            }
            finally { Application.logMessageReceived -= reenter; }
        }

        [Test]
        public void RepeatedQuitAndShutdownEmitOneBlockedResult()
        {
            Invoke("OnWantsToQuit");
            Invoke("OnWantsToQuit");
            Invoke("OnApplicationQuit");
            Invoke("OnDisable");
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(PortalCheckPolicyTests.Field(results[0], "status"), Is.EqualTo("Blocked"));
            Assert.That(Environment.ExitCode, Is.EqualTo(2));
        }

        [Test]
        public void PendingFailureSurvivesRepeatedEarlyQuit()
        {
            run.GetType().GetMethod("RecordFailure").Invoke(run, new object[] { "capture failed before quit" });
            Invoke("OnWantsToQuit");
            Invoke("OnWantsToQuit");
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(PortalCheckPolicyTests.Field(results[0], "status"), Is.EqualTo("Failed"));
            Assert.That(PortalCheckPolicyTests.Field(results[0], "failureReason"), Is.EqualTo("capture failed before quit"));
            Assert.That(Environment.ExitCode, Is.EqualTo(1));
        }
    }
}
