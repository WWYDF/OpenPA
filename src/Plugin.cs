using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using System.IO.MemoryMappedFiles;
using System.Text;
using BepInEx.Configuration;
using Agents;
using GTFO.API;
using SNetwork;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using Player;
using OpenUtils;

namespace OpenPA3
{

    public class OpenPAConfig
    {
        public static ConfigEntry<bool>? configTalkState;
        public static ConfigEntry<bool>? configVerbose;
        public static ConfigEntry<bool> configHardMode;
        public static ConfigEntry<int> configIntensity { get; set; }
    }

    [BepInPlugin("com.blals.openpa", "PositionalAudio", "3.0.0")]
    public class Plugin : BasePlugin
    {
        mumblelib.MumbleLinkFile mumbleLink;
        public bool isPlayerInLevel = false;
        Thread clientThread;
        public bool gameStarted = false;
        NoiseAgentHandler noiseHandler = new(); // This needs to be global.

        private volatile bool clientNoise = false;
        public override void Load()
        {
            OpenPAConfig.configTalkState = Config.Bind("TalkState", "Enabled", false, "Whether or not the game should tap into the TalkState plugin for Mumble.");
            OpenPAConfig.configIntensity = Config.Bind("TalkState", "Refresh Rate", 120, "The amount of time in milliseconds that the plugin will check for TalkState changes. 120 is a good sweetspot, but you can lower this if it's not precise enough. You could also up it if your host has a bad CPU, since hosts will be a bit more stressed out in this process. I would stay between 30ms and 240ms.");
            OpenPAConfig.configHardMode = Config.Bind("TalkState", "Hard Mode", false, "If enabled, sleepers will instantly wake up from talking nearby. Only the host needs this turned on.");
            OpenPAConfig.configVerbose = Config.Bind("Verbose", "Enabled", false, "Enables debug logs in the BepInEx console. Can get very spammy, but useful for debugging.");

            LevelAPI.OnEnterLevel += initStart; // open event call
            LevelAPI.OnLevelCleanup += initClose; // open event call

            sendLogger("Plugin successfully loaded.", "info", false);

        }




        // █▄ ▄█ █ █ █▄ ▄█ █▄▄ █   █▀▀    --    █▀▀ ▀█▀ ▄▀▄ █▀█ ▀█▀
        // █ ▀ █ █▄█ █ ▀ █ █▄█ █▄▄ ██▄    --    ▄██  █  █▀█ █▀▄  █ 

        public unsafe bool startMumble()
        {
            mumbleLink = mumblelib.MumbleLinkFile.CreateOrOpen();
            mumblelib.Frame* frame = mumbleLink.FramePtr();
            frame->SetName("GTFO");
            frame->uiVersion = 2;
            string id = OPAUtils.RandomString(16);
            frame->SetID(id);
            frame->SetContext("InLevel");
            sendLogger($"Mumble Setup Successful. UserID is {id}, and they are now in context 'InLevel'.", "info", false);
            return true;
        }

        public async void initStart()
        {
            isPlayerInLevel = true;
            sendLogger("Level started! Initializing Mumble...", "info", false);
            // Run Mumble Setup
            if (!startMumble())
            {
                initStart(); // If startMumble() fails, run initStart again i guess lol
                return;
            }

            if (OpenPAConfig.configTalkState.Value) // If TalkState is enabled, set it up.
            {
                sendLogger("TalkState enabled. Starting TalkState...", "info", false);
                PlayerStatus.PlayerStartedTalking(0, false);
                PlayerStatus.PlayerStatusChanged += PlayerStatusChangedHandler;

                HostSync();
                initTalkState();
            }

            sendLogger("Connecting to Mumble...", "info", false);
            PlayerAgent character = Player.PlayerManager.GetLocalPlayerAgent();

            while (character != null && GameStateManager.CurrentStateName.ToString() != null && isPlayerInLevel == true) // Basically, while (true), send positional data to Mumble.
            {
                sendToMumble(character, character.EyePosition - new Vector3(0, 1, 0), character.FPSCamera);
                await Task.Delay(12);
            }
        }

        public unsafe void sendToMumble(PlayerAgent character, Vector3 position, FPSCamera usercam)
        {
            sendLogger($"X={position.x}, Y={position.y}, Z={position.z}, RotX={usercam.Forward.x}, RotY={usercam.Forward.y}, RotZ={usercam.Forward.z}", "debug", true);

            mumblelib.Frame* frame = mumbleLink.FramePtr();

            // Send Rotational Data
            frame->fCameraPosition[0] = usercam.Position.x;
            frame->fCameraPosition[1] = usercam.Position.y;
            frame->fCameraPosition[2] = usercam.Position.z;

            frame->fCameraFront[0] = usercam.Forward.x;
            frame->fCameraFront[1] = usercam.Forward.y;
            frame->fCameraFront[2] = usercam.Forward.z;

            // Send Positional Data
            frame->fAvatarPosition[0] = position.x;
            frame->fAvatarPosition[1] = position.y;
            frame->fAvatarPosition[2] = position.z;

            // Send to MumbleLinkFile.cs.
            frame->uiTick++;
        }


        public void initClose()
        {
            sendLogger("Level closed! Closing Link Connection...", "info", false);
            isPlayerInLevel = false; // Tell initStart() to stop looping.
            mumbleLink.Dispose();
            mumbleLink = null;
        }





        // ▀█▀ ▄▀▄ █   █▄▀ █▀▀ ▀█▀ ▄▀▄ ▀█▀ █▀▀ 
        //  █  █▀█ █▄▄ █ █ ▄██  █  █▀█  █  ██▄

        public async void initTalkState()
        {
            try
            {
                using (MemoryMappedFile.OpenExisting("posaudio_mumlink")) { sendLogger("TalkState loaded, and MemoryMappedFile was found. Continuing with operation.", "info", false); }
            }
            catch (FileNotFoundException)
            {
                sendLogger("TalkState loaded, but MemoryMappedFile could not be found. Disabling TalkState for this session.", "error", false);
                return;
            }

            // Continue with operation after trycatch.
            bool sendStartOnce = false;
            bool sendStopOnce = false;

            while (isPlayerInLevel) // Only continue if player is in level.
            {
                using (var mmf = MemoryMappedFile.OpenExisting("posaudio_mumlink"))
                {

                    using (var accessor = mmf.CreateViewAccessor(0, 1024))
                    {
                        byte[] dataBytes = new byte[1024];
                        accessor.ReadArray(0, dataBytes, 0, 1024);

                        string receivedData = Encoding.UTF8.GetString(dataBytes).TrimEnd('\0'); // Put received data into a string and trim the null character.

                        NoiseAgent(receivedData);
                    }
                }


                await Task.Delay(OpenPAConfig.configIntensity.Value);
            }
        }

        public void NoiseAgent(string receivedData)
        {
            if (SNet.LocalPlayer.IsMaster) // If Player is the Host
            {
                if (receivedData == "Talking")
                {
                    if (OpenPAConfig.configHardMode.Value) // If Hardmode is enabled
                        Player.PlayerManager.GetLocalPlayerAgent().Noise = Agent.NoiseType.LoudLanding;
                    else
                        Player.PlayerManager.GetLocalPlayerAgent().Noise = Agent.NoiseType.Walk;
                    sendLogger("[NoiseAgent] Local Player is now audible.", "debug", true);
                    return;
                } // Since player is host, it doesn't need to be cancelled. (weird game logic, idk either)
                return;
            }
            
            // "else" (client stuff below)
            noiseHandler.ClientNoiseAgent(receivedData);
        }

        





        // █▀▀ ▀█▀ █▀▀ ▄▀▄ █▄ ▄█    █▄ █ █▀▀ ▀█▀ █ █ █ █▀█ █▀█ █▄▀ ▀█▀ █▄ █ █▀▀
        // ▄██  █  ██▄ █▀█ █ ▀ █    █ ▀█ ██▄  █  ▀▄▀▄▀ █▄█ █▀▄ █ █ ▄█▄ █ ▀█ █▄█


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct ClientSendData
        {
            public int clientSlot;
            public bool clientStatus;
        }

        public void HostSync()
        {
            if (NetworkAPI.IsEventRegistered("Client_Status") == true) { return; } // prevent opening more than one event handler.

            NetworkAPI.RegisterEvent<ClientSendData>("Client_Status", (senderId, packet) =>
            {
                if (!SNet.LocalPlayer.IsMaster) return; // Make sure player is host.

                sendLogger($"Message receive from {senderId}: {packet.clientSlot}, {packet.clientStatus}.", "debug", true);

                switch (packet.clientStatus)
                {
                    case true:
                        PlayerStatus.PlayerStartedTalking(packet.clientSlot, true); break;

                    case false:
                        PlayerStatus.PlayerStoppedTalking(packet.clientSlot); break;
                }

                sendLogger("[HostSync] Triggered NetworkEvent.", "debug", true);
            });
            sendLogger("[HostSync] Registered NetworkEvent.", "info", false);
        }

        public void PlayerStatusChangedHandler(object sender, PlayerStatusChangedEvent e)
        {
            int cSlot = e.PlayerID;
            PlayerAgent clientAgent = null;

            if (e.IsTalking)
            {
                sendLogger($"Player {e.PlayerID} started talking.", "debug", true);
                bool trygetresult = Player.PlayerManager.TryGetPlayerAgent(ref cSlot, out clientAgent);
                if (trygetresult)
                {
                    clientAgent.Noise = Agent.NoiseType.Walk;
                }
            }
            else
            {
                sendLogger($"Player {e.PlayerID} stopped talking.", "debug", true);
            }
        }




        // █▄ ▄█ ▀█▀ █▀▀ █▀▀ 
        // █ ▀ █ ▄█▄ ▄██ █▄▄ 


        public void sendLogger(string msg, string type, bool verbose)
        {
            if (verbose) // This message is verbose
            {
                if (!OpenPAConfig.configVerbose.Value) // Player has verbose off.
                {
                    return;
                }
            }

            // Continue if message isn't verbose, or player has verbose on.
            switch (type)
            {
                case "error":
                    Log.LogError(msg); break;
                case "info":
                    Log.LogInfo(msg); break;
                case "debug":
                    Log.LogDebug(msg); break;
                case "warning":
                    Log.LogWarning(msg); break;

            }
        }
    }

    public static class PlayerStatus
    {

        private static ConcurrentDictionary<int, bool> playerStatus = new ConcurrentDictionary<int, bool>();

        public static event EventHandler<PlayerStatusChangedEvent> PlayerStatusChanged;

        public static void PlayerStartedTalking(int playerID, bool isTalking)
        {
            playerStatus.AddOrUpdate(playerID, isTalking, (key, oldValue) => isTalking);

            // Raise changed event
            OnPlayerStatusChanged(new PlayerStatusChangedEvent(playerID, isTalking));
        }

        public static void PlayerStoppedTalking(int playerID)
        {
            bool removed = playerStatus.TryRemove(playerID, out _);
        }

        public static List<int> GetPlayersTalking()
        {
            return playerStatus.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
        }

        private static void OnPlayerStatusChanged(PlayerStatusChangedEvent e)
        {
            PlayerStatusChanged?.Invoke(null, e);
        }
    }

    public class PlayerStatusChangedEvent : EventArgs
    {
        public int PlayerID { get; }
        public bool IsTalking { get; }

        public PlayerStatusChangedEvent(int playerID, bool isTalking)
        {
            PlayerID = playerID;
            IsTalking = isTalking;
        }
    }
}