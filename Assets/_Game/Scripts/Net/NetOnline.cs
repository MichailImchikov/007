using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Internet games through Unity Gaming Services (package com.unity.services.multiplayer): the host creates a session
    /// — a public, listed game with a short join code — whose traffic goes through Unity Relay, so nobody needs an open
    /// port or a public IP. Others pick it from the list ("Найти игру" → Интернет) or type the code.
    ///
    /// The session only carries the connection: <see cref="RelayNetHandler"/> plugs the relay allocation into our own
    /// <see cref="NetGame"/> (same NetworkManager, same named messages, same approval with the nick and look), so the
    /// game itself runs exactly as on the LAN.
    ///
    /// Needs the project linked to a Unity Cloud project with Relay and Lobby (Multiplayer Sessions) enabled; players
    /// sign in anonymously. Each machine keeps its own anonymous player — a second copy of the game on the same computer
    /// needs <c>-uprofile NAME</c> on its command line (the editor uses its own profile).
    /// </summary>
    public static class NetOnline
    {
        public struct Game
        {
            public string id, name, map, host;
            public GameMode mode;
            public int players, max;
        }

        /// <summary>The session this machine is in (null = none).</summary>
        public static ISession Session { get; private set; }
        /// <summary>Join code of the current session (the host shows it in the pause menu and the scoreboard).</summary>
        public static string Code => Session != null ? Session.Code : null;
        /// <summary>A create / join request is in flight.</summary>
        public static bool Working { get; private set; }
        /// <summary>Open internet games from the last <see cref="Refresh"/>.</summary>
        public static readonly List<Game> Found = new List<Game>();
        public static bool Searching { get; private set; }
        /// <summary>The last list request failed (services unavailable): the reason, else null.</summary>
        public static string ListError { get; private set; }

        const string SessionType = "fps007";
        static float lastQuery = -99f;
        static bool leaving;

        // ------------------------------------------------------------------ sign-in

        static async Task<bool> SignIn()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    var opts = new InitializationOptions();
                    opts.SetProfile(Profile());
                    await UnityServices.InitializeAsync(opts);
                }
                if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Online] sign-in failed: " + e);
                NetGame.Say("Сервисы Unity недоступны: " + Reason(e));
                ListError = Reason(e);
                return false;
            }
        }

        /// <summary>The anonymous player of this copy of the game: -uprofile NAME, else the editor's or the player's.</summary>
        static string Profile()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-uprofile")
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (char ch in args[i + 1]) if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') sb.Append(ch);
                    if (sb.Length > 0) return sb.ToString().Substring(0, Mathf.Min(30, sb.Length));
                }
            return Application.isEditor ? "editor" : "player";
        }

        // ------------------------------------------------------------------ host / join

        /// <summary>Main menu → "Создать игру" → Интернет: a listed internet game on <paramref name="scene"/>.</summary>
        public static async void Host(string scene, string mapTitle)
        {
            if (Working) return;
            Working = true;
            try
            {
                NetGame.Say("Вход в сервисы Unity...");
                if (!await SignIn()) return;
                await Reset();
                NetGame.Say("Создание интернет-игры...");
                string nick = CharacterLook.Local.nick;
                var options = new SessionOptions
                {
                    Name = Clip(nick + " · " + mapTitle, 60),
                    MaxPlayers = NetGame.MaxPlayers,
                    IsPrivate = false,
                    Type = SessionType,
                    SessionProperties = new Dictionary<string, SessionProperty>
                    {
                        // indexed: the list filters by game version and shows the map / mode
                        ["ver"] = new SessionProperty(NetGame.Protocol.ToString(), VisibilityPropertyOptions.Public, PropertyIndex.String1),
                        ["map"] = new SessionProperty(scene, VisibilityPropertyOptions.Public, PropertyIndex.String2),
                        ["mode"] = new SessionProperty(((int)MatchSettings.mode).ToString(), VisibilityPropertyOptions.Public, PropertyIndex.String3),
                        ["host"] = new SessionProperty(nick, VisibilityPropertyOptions.Public),
                    },
                }.WithRelayNetwork().WithNetworkHandler(new RelayNetHandler { scene = scene });
                Session = await MultiplayerService.Instance.CreateSessionAsync(options);
                string code = Session.Code;
                GUIUtility.systemCopyBuffer = code;
                NetGame.Say($"Интернет-игра создана. Код: {code} (скопирован в буфер)");
                if (MatchHUD.Instance) MatchHUD.Instance.Notice($"Код игры: {code} — скопирован, отправьте друзьям");
                Debug.Log($"[Online] hosting session {Session.Id}, code {code}");
            }
            catch (Exception e) { Failed("Не удалось создать интернет-игру", e); }
            finally { Working = false; }
        }

        /// <summary>"Найти игру" → Интернет → the code typed in.</summary>
        public static async void JoinByCode(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant().Replace(" ", "");
            if (code.Length < 4) { NetGame.Say("Введите код игры"); return; }
            if (Working) return;
            Working = true;
            try
            {
                NetGame.Say("Вход в сервисы Unity...");
                if (!await SignIn()) return;
                await Reset();
                NetGame.Say("Подключение по коду " + code + "...");
                Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code, JoinOptions());
                Debug.Log($"[Online] joined session {Session.Id} by code {code}");
            }
            catch (Exception e) { Failed("Не удалось войти по коду", e); }
            finally { Working = false; }
        }

        /// <summary>"Найти игру" → Интернет → a game from the list.</summary>
        public static async void JoinById(string id)
        {
            if (string.IsNullOrEmpty(id) || Working) return;
            Working = true;
            try
            {
                if (!await SignIn()) return;
                await Reset();
                NetGame.Say("Подключение к интернет-игре...");
                Session = await MultiplayerService.Instance.JoinSessionByIdAsync(id, JoinOptions());
                Debug.Log($"[Online] joined session {Session.Id}");
            }
            catch (Exception e) { Failed("Не удалось подключиться", e); }
            finally { Working = false; }
        }

        static JoinSessionOptions JoinOptions() => new JoinSessionOptions { Type = SessionType }.WithNetworkHandler(new RelayNetHandler());

        /// <summary>Before a new session: leave the old one and let the previous connection close.</summary>
        static async Task Reset()
        {
            if (Session != null) await LeaveNow();
            NetGame.Leave();
            await NetGame.Ensure().WaitIdle();
        }

        // ------------------------------------------------------------------ list

        /// <summary>Re-reads the open internet games (at most every 4 s; the service limits how often it may be asked).</summary>
        public static async void Refresh()
        {
            if (Working || Searching || Time.realtimeSinceStartup - lastQuery < 4f) return;
            Searching = true;
            lastQuery = Time.realtimeSinceStartup;
            try
            {
                if (!await SignIn()) return;
                var q = new QuerySessionsOptions
                {
                    Count = 20,
                    FilterOptions = new List<FilterOption>
                    {
                        new FilterOption(FilterField.StringIndex1, NetGame.Protocol.ToString(), FilterOperation.Equal),
                        new FilterOption(FilterField.AvailableSlots, "0", FilterOperation.Greater),
                    },
                    SortOptions = new List<SortOption> { new SortOption(SortOrder.Descending, SortField.CreationTime) },
                };
                var res = await MultiplayerService.Instance.QuerySessionsAsync(q);
                Found.Clear();
                foreach (var s in res.Sessions)
                {
                    if (s == null || (Session != null && s.Id == Session.Id)) continue;
                    var g = new Game { id = s.Id, name = s.Name, max = s.MaxPlayers, players = s.MaxPlayers - s.AvailableSlots };
                    if (s.Properties != null)
                    {
                        if (s.Properties.TryGetValue("map", out var m)) g.map = m.Value;
                        if (s.Properties.TryGetValue("host", out var h)) g.host = h.Value;
                        if (s.Properties.TryGetValue("mode", out var md) && int.TryParse(md.Value, out int mi)) g.mode = (GameMode)mi;
                    }
                    Found.Add(g);
                }
                ListError = null;
            }
            catch (Exception e)
            {
                ListError = Reason(e);
                Debug.LogWarning("[Online] list failed: " + e.Message);
            }
            finally { Searching = false; }
        }

        // ------------------------------------------------------------------ leaving

        /// <summary><see cref="NetGame"/> shut down (menu, error, quit): leave the session — the host closes it for everybody.</summary>
        public static void OnNetShutdown()
        {
            if (Session == null || leaving) return;
            _ = LeaveNow();
        }

        static async Task LeaveNow()
        {
            var s = Session;
            Session = null;
            if (s == null) return;
            leaving = true;
            try
            {
                if (s.IsHost) await s.AsHost().DeleteAsync();
                else await s.LeaveAsync();
            }
            catch (Exception e) { Debug.LogWarning("[Online] leave: " + e.Message); }
            finally { leaving = false; }
        }

        // ------------------------------------------------------------------ messages

        static void Failed(string what, Exception e)
        {
            Debug.LogWarning("[Online] " + what + ": " + e);
            if (Session != null) _ = LeaveNow();
            NetGame.Leave();
            NetGame.Say(what + ": " + Reason(e));
        }

        /// <summary>A short Russian reason for a service error.</summary>
        static string Reason(Exception e)
        {
            if (e is SessionException se)
            {
                switch (se.Error)
                {
                    case SessionError.SessionNotFound:
                    case SessionError.InvalidSessionIdentifier: return "игра не найдена (проверьте код)";
                    case SessionError.SessionDeleted: return "игра уже закрыта";
                    case SessionError.NotAuthorized:
                    case SessionError.Forbidden: return "нет доступа — проверьте, что проект привязан к Unity Cloud и в нём включены Relay и Lobby";
                    case SessionError.RateLimitExceeded: return "слишком частые запросы, подождите пару секунд";
                    case SessionError.NetworkSetupFailed:
                    case SessionError.NetworkManagerStartFailed: return "не удалось поднять соединение через Relay";
                }
            }
            if (e is RequestFailedException rf)
            {
                if (rf.ErrorCode == CommonErrorCodes.Forbidden || rf.ErrorCode == 401 || rf.ErrorCode == 403)
                    return "нет доступа — проверьте, что проект привязан к Unity Cloud и в нём включены Relay и Lobby";
                if (rf.ErrorCode == CommonErrorCodes.TransportError || rf.ErrorCode == CommonErrorCodes.Timeout) return "нет связи с серверами Unity (интернет?)";
            }
            string m = e.Message;
            if (e.InnerException != null && !string.IsNullOrEmpty(e.InnerException.Message)) m = e.InnerException.Message;
            return Clip(string.IsNullOrEmpty(m) ? e.GetType().Name : m, 140);
        }

        static string Clip(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n);
    }

    /// <summary>
    /// Puts the relay connection the session reserved into our own <see cref="NetGame"/> (instead of the package's
    /// built-in Netcode integration): host — start hosting on the relay and load the map; client — connect through the
    /// relay, the host's Welcome loads the map as on the LAN.
    /// </summary>
    class RelayNetHandler : INetworkHandler
    {
        /// <summary>Host only: the map to load once the relay is up.</summary>
        public string scene;

        public async Task StartAsync(NetworkConfiguration configuration)
        {
            var ng = NetGame.Ensure();
            await ng.WaitIdle();
            bool ok = configuration.Role == NetworkRole.Client
                ? ng.BeginRelayJoin(configuration.RelayServerData)
                : ng.BeginRelayHost(configuration.RelayServerData, scene);
            if (!ok) throw new SessionException(NetGame.Status, SessionError.NetworkSetupFailed, null);
        }

        public Task StopAsync()
        {
            // the session ended under us (the host closed it, we were removed): drop the connection too
            if (NetGame.I) NetGame.I.OnlineStopped();
            return Task.CompletedTask;
        }
    }
}
