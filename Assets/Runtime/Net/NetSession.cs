using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace ZombieGame
{
    public enum NetRole { None, Host, Client }

    /// <summary>
    /// The online connection for two-human Co-op (D-170): Netcode for GameObjects as the transport layer, with either a
    /// Unity Relay join code (Multiplayer Services sessions) or a direct IP address. Game state is sent as our own
    /// messages (<see cref="NetHost"/>, <see cref="NetClient"/>), not as network objects. Host-authoritative.
    /// </summary>
    public class NetSession : MonoBehaviour
    {
        public const ushort DefaultPort = 7777;
        public static NetSession Instance { get; private set; }

        public NetRole Role { get; private set; }
        /// <summary>Host: the friend has joined and said hello. Client: connected to the Host.</summary>
        public bool PeerConnected { get; private set; }
        public bool InMatch { get; set; }
        public string Status { get; private set; } = "";
        public string JoinCode { get; private set; }
        public const uint NetworkTickRate = 60;

        public string LocalAddress { get; private set; }
        /// <summary>Other addresses this device has, in case the first isn't on the friend's network.</summary>
        public string OtherAddresses { get; private set; } = "";

        /// <summary>
        /// True when join codes can work: the project is linked to Unity Gaming Services (Relay). Owner, 2026-10-07: the
        /// "Host online" button was unusable because it isn't linked yet.
        /// </summary>
        public static bool OnlineAvailable => !string.IsNullOrEmpty(Application.cloudProjectId);

        private static string _gameVersion;

        /// <summary>
        /// The fingerprint of this build's game code (owner, 2026-10-08: two different builds can't play together; their
        /// updates don't read). Written into each build (Resources/BuildStamp); in the editor worked out from the code, so
        /// the editor matches a build made from the same code.
        /// </summary>
        public static string GameVersion
        {
            get
            {
                if (_gameVersion != null) return _gameVersion;
#if UNITY_EDITOR
                _gameVersion = CodeFingerprint(System.IO.Path.GetFullPath("Assets/Zombie/Code/Runtime"));
#else
                var stamp = Resources.Load<TextAsset>("BuildStamp");
                _gameVersion = stamp != null ? stamp.text.Trim() : "unknown";
#endif
                return _gameVersion;
            }
        }

        /// <summary>A short hash of every C# file under <paramref name="folder"/> (names and contents, line endings ignored).</summary>
        public static string CodeFingerprint(string folder)
        {
            var files = System.IO.Directory.GetFiles(folder, "*.cs", System.IO.SearchOption.AllDirectories);
            System.Array.Sort(files, System.StringComparer.Ordinal);
            using var sha = System.Security.Cryptography.SHA1.Create();
            var all = new System.Text.StringBuilder();
            foreach (var f in files)
            {
                all.Append(System.IO.Path.GetFileName(f)).Append('\n');
                all.Append(System.IO.File.ReadAllText(f).Replace("\r\n", "\n")).Append('\n');
            }
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(all.ToString()));
            var hex = new System.Text.StringBuilder();
            for (var i = 0; i < 6; i++) hex.Append(hash[i].ToString("x2"));
            return hex.ToString();
        }

        public const string VersionMismatch = "The two devices have different versions of the game. Install the same build on both.";

        /// <summary>The round trip to the other player in milliseconds (-1: not connected), shown in online games.</summary>
        public int PingMs
        {
            get
            {
                if (_transport == null || !PeerConnected) return -1;
                try
                {
                    var other = Role == NetRole.Host ? PeerClientId : NetworkManager.ServerClientId;
                    return other == ulong.MaxValue ? -1 : (int)_transport.GetCurrentRtt(other);
                }
                catch (Exception)
                {
                    return -1;
                }
            }
        }
        public ulong PeerClientId { get; private set; } = ulong.MaxValue;
        /// <summary>Identifies a player across reconnects (D-180).</summary>
        public string LocalPlayerId { get; private set; }
        public string PeerPlayerId { get; private set; }
        public bool Busy { get; private set; }
        /// <summary>Host: the character the friend chose in the lobby (index into the character list).</summary>
        public int PeerCharacter { get; private set; }
        /// <summary>Client: the character this player chose; sent to the Host whenever it changes.</summary>
        public int LocalCharacter { get; private set; }

        public void ChooseCharacter(int index)
        {
            LocalCharacter = index;
            if (Role != NetRole.Client || !PeerConnected) return;
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe(index);
            SendToHost(Wire.Character, w, NetworkDelivery.ReliableSequenced);
        }

        public NetHost HostSide { get; private set; }
        public NetClient ClientSide { get; private set; }

        /// <summary>Raised on the client when the Host goes away; true when it happened mid-match (host migration, D-172).</summary>
        public event Action<bool> HostLost;
        /// <summary>Raised on the client for a lobby message from the Host (the Host is back in the lobby).</summary>
        public event Action BackToLobby;

        private NetworkManager _network;
        private UnityTransport _transport;
        private ISession _session;

        public static NetSession Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Net Session");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<NetSession>();
            return Instance;
        }

        private void Awake()
        {
            Instance = this;
            HostSide = gameObject.AddComponent<NetHost>();
            ClientSide = gameObject.AddComponent<NetClient>();
            LocalPlayerId = CommandLineValue("-zombie-player-id") ?? PersistentPlayerId();
            _network = gameObject.AddComponent<NetworkManager>();
            _transport = gameObject.AddComponent<UnityTransport>();
            // Owner, 2026-10-08 (lowering the lag): messages leave at the network tick, so 60 a second instead of the default
            // 30 (each waits at most ~16 ms instead of ~33 before it goes).
            _network.NetworkConfig = new NetworkConfig { NetworkTransport = _transport, EnableSceneManagement = false, ConnectionApproval = false, TickRate = NetworkTickRate };
            _network.OnClientConnectedCallback += OnClientConnected;
            _network.OnClientDisconnectCallback += OnClientDisconnected;
            _network.OnTransportFailure += OnTransportFailure;
        }

        private static string PersistentPlayerId()
        {
            var id = PlayerPrefs.GetString("zombie.playerId", "");
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString("zombie.playerId", id);
                PlayerPrefs.Save();
            }
            return id;
        }

        public static string CommandLineValue(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // ------------------------------------------------------------------ start / stop

        /// <summary>Host on this device's address (same network, or a forwarded port).</summary>
        /// <summary>How this device last connected: Relay join code (true) or direct address. A migrating Host reuses it.</summary>
        public bool LastWasOnline { get; private set; }

        /// <summary>
        /// Host migration (D-172): host the match this device already runs, so the player who dropped can rejoin into
        /// their character (D-180). <paramref name="peerPlayerId"/> is theirs.
        /// </summary>
        public void Rehost(string peerPlayerId, System.Action ready)
        {
            if (LastWasOnline) HostOnline(() => Adopt(peerPlayerId, ready));
            else
            {
                HostDirect();
                if (Role == NetRole.Host) Adopt(peerPlayerId, ready);
            }
        }

        private void Adopt(string peerPlayerId, System.Action ready)
        {
            PeerPlayerId = peerPlayerId;
            Status = "You are now the host. Waiting for your friend to rejoin";
            ready?.Invoke();
        }

        public void HostDirect(ushort port = DefaultPort)
        {
            Leave();
            _transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
            var addresses = LocalAddresses();
            LocalAddress = $"{(addresses.Count > 0 ? addresses[0] : "127.0.0.1")}:{port}";
            OtherAddresses = addresses.Count > 1 ? string.Join(", ", addresses.GetRange(1, Mathf.Min(2, addresses.Count - 1))) : "";
            if (!_network.StartHost())
            {
                Status = "Couldn't start hosting.";
                return;
            }
            BecomeHost();
            LastWasOnline = false;
            Status = "Waiting for your friend to join";
        }

        /// <summary>
        /// What the player typed or pasted, cleaned (owner, 2026-10-07: a phone keyboard put a space after every dot,
        /// "192. 168. 68. 127", and joining failed): no spaces, commas read as dots.
        /// </summary>
        public static string CleanAddress(string text)
        {
            var clean = new System.Text.StringBuilder();
            foreach (var ch in text ?? "")
            {
                if (char.IsWhiteSpace(ch)) continue;
                clean.Append(ch == ',' || ch == '。' ? '.' : ch);
            }
            return clean.ToString();
        }

        /// <summary>True when the text is an IPv4 address, optionally with a port (what "Host on this network" shows).</summary>
        public static bool IsAddress(string text)
        {
            var parts = CleanAddress(text).Split(':');
            if (parts.Length > 2 || parts.Length == 2 && !ushort.TryParse(parts[1], out _)) return false;
            var numbers = parts[0].Split('.');
            if (numbers.Length != 4) return false;
            foreach (var n in numbers)
                if (!byte.TryParse(n, out _)) return false;
            return true;
        }

        public void JoinDirect(string address)
        {
            Leave();
            address = CleanAddress(address);
            if (!IsAddress(address))
            {
                Status = $"\"{address}\" isn't an address. It looks like 192.168.1.20:7777.";
                return;
            }
            var parts = address.Split(':');
            var port = parts.Length > 1 && ushort.TryParse(parts[1], out var p) ? p : DefaultPort;
            _transport.SetConnectionData(parts[0], port);
            if (!_network.StartClient())
            {
                Status = "Couldn't connect.";
                return;
            }
            BecomeClient();
            LastWasOnline = false;
            Status = $"Connecting to {parts[0]}:{port}";
        }

        /// <summary>Host through Unity Relay; the friend joins with the short code (needs Unity Gaming Services).</summary>
        public async void HostOnline() => HostOnline(null);

        private async void HostOnline(System.Action ready)
        {
            Leave();
            Busy = true;
            Status = "Creating an online game";
            try
            {
                await SignIn();
                _session = await MultiplayerService.Instance.CreateSessionAsync(new SessionOptions { MaxPlayers = 2 }.WithRelayNetwork());
                JoinCode = _session.Code;
                BecomeHost();
                Status = "Waiting for your friend to join";
                LastWasOnline = true;
                ready?.Invoke();
            }
            catch (Exception e)
            {
                Status = OnlineError(e);
                Leave();
            }
            finally
            {
                Busy = false;
            }
        }

        public async void JoinOnline(string code)
        {
            Leave();
            Busy = true;
            Status = "Joining";
            try
            {
                await SignIn();
                _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(CleanAddress(code).ToUpperInvariant());
                BecomeClient();
                // Owner, 2026-10-07 (the game never started over a join code): the connection can complete during the
                // join above, before this side became the client, and then the hello was never sent.
                if (_network.IsConnectedClient) Connected();
                LastWasOnline = true;
                Status = "Connecting";
            }
            catch (Exception e)
            {
                Status = OnlineError(e);
                Leave();
            }
            finally
            {
                Busy = false;
            }
        }

        private static async System.Threading.Tasks.Task SignIn()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        private static string OnlineError(Exception e)
        {
            Debug.LogWarning($"[ZOMBIE-NET] online services: {e}");
            var text = e.Message ?? "";
            if (text.IndexOf("project", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("cloud", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Online play isn't set up yet (the project isn't linked to Unity Gaming Services). Use the IP option.";
            return $"Online error: {text}";
        }

        public void Leave()
        {
            InMatch = false;
            PeerConnected = false;
            PeerClientId = ulong.MaxValue;
            PeerPlayerId = null;
            JoinCode = null;
            if (_session != null)
            {
                try { _session.LeaveAsync(); } catch (Exception) { /* already gone */ }
                _session = null;
            }
            if (_network != null && _network.IsListening) _network.Shutdown();
            Role = NetRole.None;
            Status = "";
        }

        private void OnDestroy()
        {
            Leave();
            if (Instance == this) Instance = null;
        }

        private void BecomeHost()
        {
            Role = NetRole.Host;
            Register();
        }

        private void BecomeClient()
        {
            Role = NetRole.Client;
            Register();
        }

        // ------------------------------------------------------------------ connection events

        private void OnClientConnected(ulong clientId)
        {
            if (Role == NetRole.Host)
            {
                if (clientId == NetworkManager.ServerClientId) return;
                if (PeerConnected && clientId != PeerClientId)
                {
                    _network.DisconnectClient(clientId, "The game is full.");
                    return;
                }
                PeerClientId = clientId;
                Status = "Your friend is connecting";
            }
            else if (Role == NetRole.Client && clientId == _network.LocalClientId) Connected();
        }

        /// <summary>The client is connected: say hello (the host lists the friend only then) and send the character.</summary>
        private void Connected()
        {
            if (PeerConnected) return;
            PeerConnected = true;
            Status = "Connected. Waiting for the host to start";
            SendHello();
            ChooseCharacter(LocalCharacter);
        }

        // Backstop: connected without the event having reached this side as the client.
        private void Update()
        {
            if (Role == NetRole.Client && !PeerConnected && _network != null && _network.IsConnectedClient) Connected();
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (Role == NetRole.Host)
            {
                if (clientId != PeerClientId) return;
                PeerConnected = false;
                PeerClientId = ulong.MaxValue;
                Status = InMatch ? "Your friend lost connection" : "Your friend left";
                // D-179: their character stays where it is, and can be killed.
                if (InMatch) HostSide.OnPeerLost();
            }
            else if (Role == NetRole.Client)
            {
                var reason = _network.DisconnectReason;
                var midMatch = InMatch;
                Leave();
                Status = string.IsNullOrEmpty(reason) ? "The host left." : reason;
                HostLost?.Invoke(midMatch);
            }
        }

        private void OnTransportFailure()
        {
            if (Role == NetRole.Client) OnClientDisconnected(_network.LocalClientId);
            else
            {
                Leave();
                Status = "Connection failed.";
            }
        }

        // ------------------------------------------------------------------ messages

        private void Register()
        {
            var messaging = _network.CustomMessagingManager;
            if (Role == NetRole.Host)
            {
                messaging.RegisterNamedMessageHandler(Wire.Hello, OnHello);
                messaging.RegisterNamedMessageHandler(Wire.Input, (sender, reader) => { if (sender == PeerClientId) HostSide.OnInput(reader); });
                messaging.RegisterNamedMessageHandler(Wire.Command, (sender, reader) => { if (sender == PeerClientId) HostSide.OnCommand(reader); });
                messaging.RegisterNamedMessageHandler(Wire.Character, (sender, reader) =>
                {
                    reader.ReadValueSafe(out int index);
                    if (sender == PeerClientId) PeerCharacter = index;
                });
            }
            else
            {
                messaging.RegisterNamedMessageHandler(Wire.Start, (sender, reader) => ClientSide.OnStart(reader));
                messaging.RegisterNamedMessageHandler(Wire.Snapshot, (sender, reader) => ClientSide.OnSnapshot(reader));
                messaging.RegisterNamedMessageHandler(Wire.Roster, (sender, reader) => ClientSide.OnRoster(reader));
                messaging.RegisterNamedMessageHandler(Wire.Loot, (sender, reader) => ClientSide.OnLoot(reader));
                messaging.RegisterNamedMessageHandler(Wire.Chests, (sender, reader) => ClientSide.OnChests(reader));
                messaging.RegisterNamedMessageHandler(Wire.Events, (sender, reader) => ClientSide.OnEvents(reader));
                messaging.RegisterNamedMessageHandler(Wire.End, (sender, reader) => ClientSide.OnEnd(reader));
                messaging.RegisterNamedMessageHandler(Wire.Lobby, (sender, reader) =>
                {
                    InMatch = false;
                    Status = "Connected. Waiting for the host to start";
                    BackToLobby?.Invoke();
                });
            }
        }

        private void SendHello()
        {
            using var w = new FastBufferWriter(128, Allocator.Temp, 1024);
            w.WriteValueSafe(LocalPlayerId);
            w.WriteValueSafe(GameVersion);
            SendToHost(Wire.Hello, w, NetworkDelivery.ReliableSequenced);
        }

        private void OnHello(ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out string playerId);
            // A different (or older, version-less) build can't play with this one: say so instead of failing every update.
            var version = "";
            try
            {
                reader.ReadValueSafe(out version);
            }
            catch (Exception)
            {
                version = "older";
            }
            if (version != GameVersion)
            {
                Debug.LogWarning($"[ZOMBIE-NET] friend's game version {version}, this one {GameVersion}: refused");
                Status = VersionMismatch;
                _network.DisconnectClient(sender, VersionMismatch);
                return;
            }
            if (InMatch)
            {
                // D-180: reconnecting into the same character, if it's still alive.
                if (playerId != PeerPlayerId || !HostSide.CanRejoin())
                {
                    _network.DisconnectClient(sender, "That game has already started.");
                    return;
                }
                PeerClientId = sender;
                PeerConnected = true;
                Status = "Your friend is back";
                HostSide.OnPeerRejoined();
                return;
            }
            PeerClientId = sender;
            PeerPlayerId = playerId;
            PeerConnected = true;
            Status = "Your friend has joined";
        }

        public void SendToPeer(string name, FastBufferWriter writer, NetworkDelivery delivery)
        {
            if (PeerClientId == ulong.MaxValue || !_network.IsListening) return;
            _network.CustomMessagingManager.SendNamedMessage(name, PeerClientId, writer, delivery);
        }

        public void SendToHost(string name, FastBufferWriter writer, NetworkDelivery delivery)
        {
            if (!_network.IsListening) return;
            _network.CustomMessagingManager.SendNamedMessage(name, NetworkManager.ServerClientId, writer, delivery);
        }

        /// <summary>
        /// This device's IPv4 addresses, the one a friend on the same network can reach first: home-network ranges
        /// (192.168.x, 10.x, 172.16-31.x) on Wi-Fi or Ethernet with a gateway; virtual adapters (Hyper-V, VirtualBox,
        /// VMware, VPNs), mobile data (100.64.x carrier addresses) and self-assigned 169.254.x last. Owner, 2026-10-07: the
        /// first adapter found was shown, which on a PC is often a virtual one and on a phone can be mobile data.
        /// </summary>
        public static System.Collections.Generic.List<string> LocalAddresses()
        {
            var found = new System.Collections.Generic.List<(string address, int score)>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var name = (nic.Name + " " + nic.Description).ToLowerInvariant();
                    var props = nic.GetIPProperties();
                    var gateway = false;
                    foreach (var g in props.GatewayAddresses)
                        if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.ToString().StartsWith("0.")) gateway = true;
                    foreach (var unicast in props.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var text = unicast.Address.ToString();
                        var b = unicast.Address.GetAddressBytes();
                        var score = 0;
                        if (b[0] == 192 && b[1] == 168 || b[0] == 10 || b[0] == 172 && b[1] >= 16 && b[1] <= 31) score += 100;
                        if (b[0] == 169 && b[1] == 254) score -= 200;
                        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) score -= 100;
                        if (name.Contains("wlan") || name.Contains("wi-fi") || name.Contains("wifi") || name.Contains("wireless") ||
                            name.Contains("eth") || name.Contains("en0") || name.Contains("en1")) score += 30;
                        if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 20;
                        if (gateway) score += 40;
                        foreach (var bad in new[] { "virtual", "vethernet", "hyper-v", "vmware", "virtualbox", "vpn", "tap", "tun", "docker", "wsl", "bluetooth", "ccmni", "rmnet", "pdp" })
                            if (name.Contains(bad)) score -= 150;
                        found.Add((text, score));
                    }
                }
            }
            catch (Exception)
            {
                // Some platforms don't allow listing interfaces.
            }
            found.Sort((x, y) => y.score.CompareTo(x.score));
            var list = new System.Collections.Generic.List<string>();
            foreach (var f in found) if (!list.Contains(f.address)) list.Add(f.address);
            return list;
        }
    }
}
