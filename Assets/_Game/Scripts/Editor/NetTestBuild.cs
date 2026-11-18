using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>
    /// Small player build for network tests (MainMenu + Map_Port), usable without the editor window:
    ///   Unity.exe -batchmode -quit -projectPath F:\007 -executeMethod FPS.EditorTools.NetTestBuild.Build -netTestOut D:\out\007.exe -logFile build.log
    /// Then run two copies: 007.exe -nethost Map_Port -netbots 0 -nettest -netabil … / 007.exe -netjoin 127.0.0.1 -nettest -netabil …
    /// (see NetAutoTest.Abilities.cs).
    /// </summary>
    public static class NetTestBuild
    {
        public static void Build()
        {
            string outPath = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-netTestOut") outPath = args[i + 1];
            if (string.IsNullOrEmpty(outPath)) outPath = "Builds/NetTest/007.exe";
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Map_Port.unity" },
                locationPathName = outPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var rep = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[NetTestBuild] {rep.summary.result} errors={rep.summary.totalErrors} time={rep.summary.totalTime} -> {outPath}");
            if (Application.isBatchMode) EditorApplication.Exit(rep.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
