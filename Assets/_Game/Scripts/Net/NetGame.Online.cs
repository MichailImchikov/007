using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Internet games (<see cref="NetOnline"/>): the same host / client game, only the transport goes through a Unity
    /// Relay allocation instead of a LAN address. No LAN advertising for such a game (LAN players cannot reach a relay
    /// host directly — they join by the code or from the internet list).
    /// </summary>
    public partial class NetGame
    {
        /// <summary>The running game goes through Unity Relay (internet), not a direct LAN connection.</summary>
        public bool Online { get; private set; }

        /// <summary>Status line for the menu (also used by <see cref="NetOnline"/>).</summary>
        public static void Say(string s) => SetStatus(s);

        /// <summary>Waits until a previous NetworkManager run has fully shut down (that finishes a frame later).</summary>
        public async Task WaitIdle()
        {
            float until = Time.realtimeSinceStartup + 3f;
            while (Busy && Time.realtimeSinceStartup < until) await Task.Yield();
        }

        /// <summary>Internet game: host through the relay allocation the session reserved, then load the map.</summary>
        public bool BeginRelayHost(RelayServerData relay, string scene)
        {
            if (string.IsNullOrEmpty(scene)) { SetStatus("Карта не выбрана"); return false; }
            utp.SetRelayServerData(relay);
            nm.NetworkConfig.ConnectionData = JoinPayload();
            nm.ConnectionApprovalCallback = Approve;
            Hosting = true;
            if (!nm.StartHost())
            {
                Hosting = false;
                SetStatus("Не удалось запустить интернет-игру");
                return false;
            }
            Running = true;
            Online = true;
            LocalId = 1;
            MapScene = scene;
            Hook();
            SetStatus("Интернет-игра создана");
            LoadMapLocal(scene);
            return true;
        }

        /// <summary>Internet game: connect to the host through the relay; its Welcome loads the map.</summary>
        public bool BeginRelayJoin(RelayServerData relay)
        {
            utp.SetRelayServerData(relay);
            nm.NetworkConfig.ConnectionData = JoinPayload();
            Hosting = false;
            if (!nm.StartClient()) { SetStatus("Не удалось подключиться через интернет"); return false; }
            Running = true;
            Online = true;
            LocalId = 0;
            Hook();
            lastHostMsg = Time.unscaledTime;
            SetStatus("Подключение к интернет-игре...");
            return true;
        }

        /// <summary>The session ended from the outside (closed by its host, we were removed): back to the menu.</summary>
        public void OnlineStopped()
        {
            if (!Running || !Online) return;
            Fail(Hosting ? "Интернет-сессия завершена" : "Хост закрыл игру");
        }
    }
}
