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
        public static ConfigEntry<bool>? configHardMode;
        public static ConfigEntry<int>? configIntensity { get; set; }
    }

    [BepInPlugin("com.blals.openpa", "PositionalAudio", "3.0.0")]
    public class Plugin : BasePlugin
    {
        mumblelib.MumbleLinkFile mumbleLink;
        public bool isPlayerInLevel = false;
        Thread clientThread;
        public bool gameStarted = false;

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
            sendLogger($"Mumble Setup Successful. UesrID is {id}, and they are now in context 'InLevel'.", "info", false);
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

            sendLogger("Connecting to Mumble...", "info", false);
            PlayerAgent character = Player.PlayerManager.GetLocalPlayerAgent();

            while (character != null && GameStateManager.CurrentStateName.ToString() != null && isPlayerInLevel == true) // Basically, while (true), send positional data to Mumble.
            {
                sendToMumble(character, character.EyePosition - new Vector3(0, 1, 0), character.FPSCamera);
                await Task.Delay(120);
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

        // █▄ ▄█ █ █ █▄ ▄█ █▄▄ █   █▀▀    --    █▀▀ █   █▀█ █▀▀ █▀▀
        // █ ▀ █ █▄█ █ ▀ █ █▄█ █▄▄ ██▄    --    █▄▄ █▄▄ █▄█ ▄██ ██▄ 

        public void initClose()
        {
            sendLogger("Level closed! Closing Link Connection...", "info", false);
            isPlayerInLevel = false; // Tell initStart() to stop looping.
            mumbleLink.Dispose();
            mumbleLink = null;
        }








        // █▄ ▄█ ▀█▀ █▀▀ █▀▀ 
        // █ ▀ █ ▄█▄ ▄██ █▄▄ 


        public void sendLogger(string msg, string type, bool verbose)
        {
            if (verbose) // This message is verbose
            {
                if (OpenPAConfig.configVerbose.Value) // Player has verbose off.
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
}