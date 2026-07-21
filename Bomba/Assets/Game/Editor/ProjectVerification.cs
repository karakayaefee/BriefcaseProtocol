using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace BriefcaseProtocol.Editor
{
    public static class ProjectVerification
    {
        private const string ReportPath = "TestResults/BriefcaseProtocol.xml";

        [MenuItem("Briefcase Protocol/Tests/Run All")]
        public static void RunAllTests()
        {
            Directory.CreateDirectory("TestResults");
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new VerificationCallbacks());
            api.Execute(new ExecutionSettings(
                new Filter
                {
                    assemblyNames = new[] { "BriefcaseProtocol.Tests.EditMode" },
                    testMode = TestMode.EditMode
                },
                new Filter
                {
                    assemblyNames = new[] { "BriefcaseProtocol.Tests.PlayMode" },
                    testMode = TestMode.PlayMode
                }));
        }

        private sealed class VerificationCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                Debug.Log("BRIEFCASE_TESTS_STARTED");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                TestRunnerApi.SaveResultToFile(result, ReportPath);
                Debug.Log($"BRIEFCASE_TESTS_FINISHED status={result.TestStatus} " +
                    $"passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.TestStatus == TestStatus.Failed)
                {
                    Debug.LogError($"BRIEFCASE_TEST_FAILED {result.FullName}: {result.Message}\n{result.StackTrace}");
                }
            }
        }
    }
}
