#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Developer tool (editor only): with the command-line flag -fpsShots, walks through the menus and maps in play
    /// mode and saves screenshots (UI included) to Logs/Shots, plus a pickup report. Does nothing otherwise.
    /// </summary>
    public class UIShots : MonoBehaviour
    {
        static string Dir => Path.Combine(Application.dataPath, "../Logs/Shots");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fpsShots") < 0) return;
            var go = new GameObject("UIShots");
            DontDestroyOnLoad(go);
            go.AddComponent<UIShots>();
        }

        RenderTexture rt;
        readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

        IEnumerator Start()
        {
            Directory.CreateDirectory(Dir);
            rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            rt.Create();
            yield return Wait(1.5f);
            if (SceneManager.GetActiveScene().name != "MainMenu") { SceneManager.LoadScene("MainMenu"); yield return Wait(1.5f); }

            yield return Shot("01_menu");
            Click("ИГРАТЬ"); yield return Wait(0.6f); yield return Shot("02_play");
            Click("ЗАВОД"); yield return Wait(0.3f); yield return Shot("03_play_plant");
            MenuPanel.CloseAll(); yield return null;
            Click("Персонаж"); yield return Wait(1f); yield return Shot("04_character");
            MenuPanel.CloseAll(); yield return null;
            Click("Снаряжение"); yield return Wait(0.6f); yield return Shot("05_loadout");
            MenuPanel.CloseAll(); yield return null;
            Click("Настройки"); yield return Wait(0.6f); yield return Shot("06_settings");
            Click("Клавиши"); yield return Wait(0.4f); yield return Shot("07_keys");
            Click("Экран и звук"); yield return Wait(0.4f); yield return Shot("08_screen");
            MenuPanel.CloseAll(); yield return null;
            Click("Создать игру по сети"); yield return Wait(0.6f); yield return Shot("09_host");
            MenuPanel.CloseAll(); yield return null;

            foreach (var map in new[] { "Map_Plant", "Map_Village", "Map_Port", "Map_Town", "Map_Warehouse", "Map_Flooded" })
            {
                SceneManager.LoadScene(map);
                yield return Wait(3f);
                yield return Shot(map + "_player");
                yield return Overview(map + "_overview");
                ReportPickups(map);
                if (map == "Map_Plant")
                {
                    PauseMenu.Ensure().Toggle(); yield return Wait(0.4f); yield return Shot(map + "_pause");
                    Click("Снаряжение"); yield return Wait(0.5f); yield return Shot(map + "_pause_loadout");
                    MenuPanel.CloseAll(); PauseMenu.Instance.Toggle(); yield return Wait(0.3f);
                }
            }
            File.WriteAllText(Path.Combine(Dir, "report.txt"), log.ToString());
            Debug.Log("[FPS] UI shots done\n" + log);
            UnityEditor.EditorApplication.Exit(0);
        }

        static IEnumerator Wait(float s) { float t = 0f; while (t < s) { t += Time.unscaledDeltaTime; yield return null; } }

        void Click(string label)
        {
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Exclude))
            {
                var t = UIKit.LabelOf(b);
                if (t && t.text.Trim() == label && b.interactable) { b.onClick.Invoke(); log.AppendLine("click " + label); return; }
            }
            log.AppendLine("! button not found: " + label);
        }

        /// <summary>Renders the main camera + every overlay canvas into the texture and saves a PNG.</summary>
        IEnumerator Shot(string name, Camera cam = null)
        {
            cam ??= Camera.main;
            bool tempCam = false;
            if (!cam) { cam = new GameObject("ShotCam").AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; tempCam = true; }
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude);
            var modes = new RenderMode[canvases.Length];
            for (int i = 0; i < canvases.Length; i++)
            {
                modes[i] = canvases[i].renderMode;
                if (!canvases[i].isRootCanvas || canvases[i].renderMode != RenderMode.ScreenSpaceOverlay) continue;
                canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                canvases[i].worldCamera = cam;
                canvases[i].planeDistance = Mathf.Max(0.5f, cam.nearClipPlane + 0.05f);
            }
            var old = cam.targetTexture;
            cam.targetTexture = rt;
            yield return null;
            yield return null; // (WaitForEndOfFrame never fires in batch mode)
            cam.Render();
            Save(name);
            cam.targetTexture = old;
            for (int i = 0; i < canvases.Length; i++) if (canvases[i]) canvases[i].renderMode = modes[i];
            if (tempCam) Destroy(cam.gameObject);
        }

        IEnumerator Overview(string name)
        {
            var cam = new GameObject("OverviewCam").AddComponent<Camera>();
            // frame the spawn points (imported maps are not centred on the origin)
            var sps = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Exclude);
            var b = new Bounds(sps.Length > 0 ? sps[0].transform.position : Vector3.zero, Vector3.zero);
            foreach (var s in sps) b.Encapsulate(s.transform.position);
            float k = Mathf.Max(1f, Mathf.Max(b.size.x, b.size.z) / 95f);
            cam.transform.position = b.center + new Vector3(-55f, 62f, -72f) * k;
            cam.transform.LookAt(b.center);
            cam.fieldOfView = 48f;
            cam.farClipPlane = 400f * k;
            cam.targetTexture = rt;
            yield return null;
            yield return null; // (WaitForEndOfFrame never fires in batch mode)
            cam.Render();
            Save(name);
            Destroy(cam.gameObject);
        }

        void Save(string name)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(Dir, name + ".jpg"), tex.EncodeToJPG(82));
            Destroy(tex);
            log.AppendLine("shot " + name);
        }

        void ReportPickups(string map)
        {
            var sp = AbilitySpawner.Instance;
            var picks = FindObjectsByType<AbilityPickup>(FindObjectsInactive.Exclude);
            int bad = 0;
            foreach (var p in picks) if (sp && !sp.Reachable(p.transform.position)) bad++;
            log.AppendLine($"{map}: {picks.Length} pickups, unreachable {bad}; player primary {(PlayerHealth.Local && PlayerHealth.Local.loadout ? PlayerHealth.Local.loadout.primaryName : "?")}");
            foreach (var p in picks) log.AppendLine("   pickup at " + p.transform.position.ToString("F1"));
        }
    }
}
#endif
