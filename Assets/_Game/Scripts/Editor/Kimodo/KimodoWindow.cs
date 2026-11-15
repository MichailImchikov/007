using System.Diagnostics;
using System.IO;
using System.Linq;
using FPS.EditorTools.Anim;
using UnityEditor;
using UnityEngine;

namespace FPS.EditorTools.Kimodo
{
    /// <summary>
    /// Tools > FPS > Kimodo: NVIDIA Kimodo text-to-motion inside the editor. Starts the local bridge server, edits
    /// the clip library (prompt, duration, root speed / direction, processing), generates and imports clips into
    /// Assets/_Game/Animation/Kimodo and rebuilds the soldier Animator Controller from them.
    /// </summary>
    public class KimodoWindow : EditorWindow
    {
        KimodoLibrary lib;
        Vector2 scroll;
        string health = "?";
        double nextPoll;

        [MenuItem("Tools/FPS/Kimodo")]
        public static void Open() => GetWindow<KimodoWindow>("Kimodo").Show();

        void OnEnable() { lib = KimodoPipeline.Library(); Poll(); }

        async void Poll()
        {
            if (!lib) return;
            var h = await KimodoPipeline.CheckHealth(lib.serverUrl);
            health = h.status == "ok" ? $"готов: {h.name} ({h.device}, {h.fps} fps)" : $"{h.status} {h.error}";
            Repaint();
        }

        void Update()
        {
            if (EditorApplication.timeSinceStartup > nextPoll)
            {
                nextPoll = EditorApplication.timeSinceStartup + (KimodoPipeline.Busy ? 1.0 : 5.0);
                if (!KimodoPipeline.Busy) Poll();
                Repaint();
            }
        }

        public static void StartServer(KimodoLibrary lib)
        {
            string script = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/Kimodo/run_bridge.ps1"));
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -ExecutionPolicy Bypass -File \"{script}\" -Root \"{lib.kimodoRoot}\"",
                UseShellExecute = true,
            });
        }

        void OnGUI()
        {
            if (!lib) lib = KimodoPipeline.Library();
            EditorGUILayout.LabelField("NVIDIA Kimodo → анимации солдата", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Сервер: " + health, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Запустить", GUILayout.Width(90))) StartServer(lib);
                if (GUILayout.Button("Проверить", GUILayout.Width(90))) Poll();
            }
            EditorGUILayout.LabelField(KimodoPipeline.Busy ? "Идёт генерация: " + KimodoPipeline.Status : "Статус: " + KimodoPipeline.Status, EditorStyles.wordWrappedLabel);

            using (new EditorGUI.DisabledScope(KimodoPipeline.Busy))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Сгенерировать отмеченные")) KimodoPipeline.Run();
                if (GUILayout.Button("Собрать Animator")) MilitaryAnimBuilder.BuildAll();
                if (GUILayout.Button("Библиотека", GUILayout.Width(90))) { Selection.activeObject = lib; EditorGUIUtility.PingObject(lib); }
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            var so = new SerializedObject(lib);
            var list = so.FindProperty("presets");
            for (int i = 0; i < list.arraySize; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(e.FindPropertyRelative("enabled"), GUIContent.none, GUILayout.Width(18));
                        EditorGUILayout.PropertyField(e.FindPropertyRelative("slot"), GUIContent.none, GUILayout.Width(110));
                        EditorGUILayout.PropertyField(e.FindPropertyRelative("process"), GUIContent.none, GUILayout.Width(110));
                        EditorGUILayout.PropertyField(e.FindPropertyRelative("duration"), GUIContent.none, GUILayout.Width(45));
                        EditorGUILayout.PropertyField(e.FindPropertyRelative("velocity"), GUIContent.none, GUILayout.Width(120));
                        bool exists = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{KimodoPipeline.ClipDir}/{lib.presets[i].slot}.anim");
                        GUILayout.Label(exists ? "✔" : "—", GUILayout.Width(16));
                        using (new EditorGUI.DisabledScope(KimodoPipeline.Busy))
                            if (GUILayout.Button("Ген", GUILayout.Width(40))) { so.ApplyModifiedProperties(); KimodoPipeline.Run(new[] { lib.presets[i].slot }); }
                    }
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("prompt"), GUIContent.none);
                }
            }
            so.ApplyModifiedProperties();
            EditorGUILayout.EndScrollView();

            if (KimodoPipeline.Log.Count > 0)
                EditorGUILayout.HelpBox(string.Join("\n", KimodoPipeline.Log.Skip(Mathf.Max(0, KimodoPipeline.Log.Count - 6))), MessageType.None);
        }
    }
}
