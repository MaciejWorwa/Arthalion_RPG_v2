using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;
using UnityEditor.Rendering;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.SceneManagement;
using Unity.InferenceEngine;

namespace Arthalion.AI.Editor
{
    public sealed class CombatAIWindow : EditorWindow
    {
        private Vector2 _scroll;
        private UnityEditor.Editor _inspector;
        [MenuItem("Arthalion/AI/Training and benchmarks")]
        public static void Open() { GetWindow<CombatAIWindow>("Combat AI"); }
        private void OnGUI()
        {
            var manager = FindFirstObjectByType<ReinforcementLearningManager>();
            EditorGUILayout.HelpBox("Train: start the Python trainer, then Play. Evaluation/Inference: assign the trained ONNX model. Random Policy is a pipeline smoke test, not learned AI.", MessageType.Info);
            if (GUILayout.Button("Build local training player")) CombatAIEditor.BuildTrainingPlayer();
            if (manager == null) { EditorGUILayout.LabelField("Open BattleScene to configure the experiment."); return; }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            UnityEditor.Editor.CreateCachedEditor(manager, null, ref _inspector);
            _inspector.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
            if (Application.isPlaying)
            {
                if (!manager.IsSessionActive && GUILayout.Button("Start configured experiment")) manager.StartExperiment();
                if (manager.IsSessionActive && GUILayout.Button("Stop after this episode")) manager.RequestStop();
                if (!string.IsNullOrEmpty(manager.RunFolder) && GUILayout.Button("Open CSV metrics")) EditorUtility.RevealInFinder(manager.RunFolder);
            }
            EditorGUILayout.LabelField("Observation schema", CombatObservations.SchemaHash());
        }
    }

    public static class CombatAIEditor
    {
        public static bool BuildingTrainingPlayer { get; private set; }
        [MenuItem("Arthalion/AI/Build training player")]
        public static void BuildTrainingPlayer()
        {
            string root = Path.GetFullPath(".");
            string output = ReinforcementLearningManager.CommandLine("--ai-build-path") ?? Path.Combine(root, "Builds", "AI", "ArthalionAI.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            Directory.CreateDirectory("Assets/Resources");
            using (var hash = SHA256.Create())
            {
                string sources = string.Join("\n", Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories)
                    .OrderBy(p => p.Replace('\\', '/'), StringComparer.Ordinal)
                    .Select(p => File.ReadAllText(p).Replace("\r\n", "\n").Replace("\r", "\n")));
                File.WriteAllText("Assets/Resources/ai_rules_version.txt", BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(sources))).Replace("-", ""));
            }
            File.WriteAllText("Assets/Resources/ai_observation_schema.txt", CombatObservations.SchemaHash());
            AssetDatabase.Refresh();
            BuildReport report;
            BuildingTrainingPlayer = true;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { "Assets/Scenes/MapEditorScene.unity", "Assets/Scenes/BattleScene.unity" },
                    locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
                });
            }
            finally { BuildingTrainingPlayer = false; }
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("AI player build failed: " + report.summary.result);
            Debug.Log("[AI] Training player: " + output);
        }

        // Batch-mode integration smoke test using the actual scenes, physics and coroutines.
        public static void RunSmokeTest()
        {
            string onnx = ReinforcementLearningManager.CommandLine("--ai-onnx");
            if (!string.IsNullOrEmpty(onnx))
            {
                AssetDatabase.ImportAsset(onnx, ImportAssetOptions.ForceUpdate);
                EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity");
                var configured = UnityEngine.Object.FindFirstObjectByType<ReinforcementLearningManager>();
                configured.Model = AssetDatabase.LoadAssetAtPath<ModelAsset>(onnx);
                if (configured.Model == null) throw new InvalidOperationException("ONNX model could not be imported: " + onnx);
                configured.Mode = LearningMode.Evaluation;
                configured.RandomPolicySmokeTest = false;
            }
            double start = EditorApplication.timeSinceStartup;
            bool entered = false;
            EditorApplication.update += Check;
            EditorApplication.isPlaying = true;
            void Check()
            {
                if (EditorApplication.isPlaying) entered = true;
                var manager = UnityEngine.Object.FindFirstObjectByType<ReinforcementLearningManager>();
                if (entered && manager != null && manager.CompletedEpisodes > 0 && !manager.IsSessionActive)
                {
                    Debug.Log($"[AI TEST] Episodes={manager.CompletedEpisodes}, aborts={manager.Aborts}, commands={manager.Commands}, decisions={manager.PolicyDecisions}");
                    int result = manager.Aborts == 0 ? 0 : 1;
                    EditorApplication.update -= Check;
                    EditorApplication.playModeStateChanged += ExitCleanly;
                    EditorApplication.isPlaying = false;
                    void ExitCleanly(PlayModeStateChange change)
                    {
                        if (change != PlayModeStateChange.EnteredEditMode) return;
                        EditorApplication.playModeStateChanged -= ExitCleanly;
                        if (Unity.MLAgents.Academy.IsInitialized) Unity.MLAgents.Academy.Instance.Dispose();
                        EditorApplication.delayCall += () => EditorApplication.Exit(result);
                    }
                }
                if (EditorApplication.timeSinceStartup - start > 180)
                { Debug.LogError("[AI TEST] Timed out."); EditorApplication.update -= Check; EditorApplication.Exit(2); }
            }
        }
    }

    // The training player uses remote policies or CPU inference. Do not spend minutes
    // compiling thousands of GPU tensor kernels that this separate build never uses.
    public sealed class TrainingComputeShaderStripper : IPreprocessComputeShaders
    {
        public int callbackOrder => 0;
        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (CombatAIEditor.BuildingTrainingPlayer && AssetDatabase.GetAssetPath(shader).StartsWith("Packages/com.unity.ai.inference", StringComparison.Ordinal))
                data.Clear();
        }
    }
}
