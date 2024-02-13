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

namespace PositionalAudio
{
    public class OpenPAConfig
    {
        public static ConfigEntry<bool> configTalkState;
        public static ConfigEntry<bool> configVerbose;
        public static ConfigEntry<int> configIntensity { get; set; }
    }

    [BepInPlugin("net.devante.gtfo.positionalaudio", "PositionalAudio", "2.0.0")]
	public class Plugin : BasePlugin
	{
		mumblelib.MumbleLinkFile mumbleLink;
		private Timer gameStateCheckTimer;
		private Timer reportingTaskTimer;
		string endData;
		public bool isPlayerInLevel = false;
		
        private volatile bool clientNoise = false;

        public override void Load()
		{
            OpenPAConfig.configTalkState = Config.Bind("TalkState", "Enabled", false, "Whether or not the game should tap into the TalkState plugin for Mumble.");
            OpenPAConfig.configIntensity = Config.Bind("TalkState", "Refresh Rate", 120, "The amount of time in milliseconds that the plugin will check for TalkState changes. 120 is a good sweetspot, but you can lower this if it's not precise enough. You could also up it if your host has a bad CPU, since hosts will be a bit more stressed out in this process. I would stay between 30ms and 240ms.");
            OpenPAConfig.configVerbose = Config.Bind("Verbose", "Enabled", false, "Enables debug logs in the BepInEx console. Can get very spammy, but useful for debugging.");

            LevelAPI.OnEnterLevel += CheckIfPlayerIsInLevel; // open event call

            SendDebugLog($"Plugin is loaded!", false);

			// Set up a timer to periodically check the game state every 5 seconds
			gameStateCheckTimer = new Timer(CheckGameState, null, System.TimeSpan.Zero, System.TimeSpan.FromSeconds(3));
		}

        public struct ClientCharID
        {
            public int PlayerID;
        }

        public unsafe void CheckGameState(object state)
		{
			var cState = GameStateManager.CurrentStateName.ToString();

			if (cState == "Generating" || cState == "InLevel")
			{
				// await Task.Delay(1000);
				SendDebugLog($"Game is now in the 'Generating' OR 'InLevel' state. ({cState})", false);

				// Run Mumble Setup
				mumbleLink = mumblelib.MumbleLinkFile.CreateOrOpen();
				mumblelib.Frame* frame = mumbleLink.FramePtr();
				frame->SetName("GTFO");
				frame->uiVersion = 2;
				string id = RandomString(16);
				SendDebugLog($"Setting Mumble ID to {id}", false);
				frame->SetID(id);
				SendDebugLog($"Setting context to InLevel", false);
				frame->SetContext("InLevel");

				SendDebugLog("Mumble Link Shared Memory Initialized", false);

				if (OpenPAConfig.configTalkState.Value == true) // Check config, if TalkState support is enabled, start TalkStatePinger.
				{
					PlayerStatus.PlayerStartedTalking(0, false);

					PlayerStatus.PlayerStatusChanged += PlayerStatusChangedHandler;
                    HostSync();
					FindTalkState();
					SendDebugLog("TalkState Shared Memory Initialized", false);
				}

                // Stop the game state check timer
                gameStateCheckTimer.Change(Timeout.Infinite, Timeout.Infinite);

				// Start the ReportingTask every second
				reportingTaskTimer = new Timer(FixedUpdated, null, System.TimeSpan.Zero, System.TimeSpan.FromMilliseconds(24));
			}
			else
			{
                // Important for debugging in development builds.
                SendDebugLog($"Currently not in level. Reattempting.. ({cState})", true);

			}
		}

		// All log messages get sent down here.
		// false == normal log
		// true == debug/verbose log (only shown to user if config value is true)
		public void SendDebugLog(string msg, bool verbose)
		{
			if (verbose == true) { if (OpenPAConfig.configVerbose.Value == true) { Log.LogInfo(msg); }
			} else { Log.LogInfo(msg); }
        }

		// Might combine this with SendDebugLog later.
        public void SendErrorLog(string msg, bool verbose)
        {
            if (verbose == true)
            {
                if (OpenPAConfig.configVerbose.Value == true) { Log.LogError(msg); }
            } else { Log.LogError(msg); }

        }

        private static System.Random random = new System.Random();
		private string RandomString(int len)
		{
			const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
			return new string(Enumerable.Repeat(chars, len)
				.Select(s => s[random.Next(s.Length)]).ToArray());
		}

        public void CheckIfPlayerIsInLevel()
		{
            SendDebugLog($"Player connected to level.", true);
			isPlayerInLevel = true;
        }

        private unsafe void FixedUpdated(object state)
		{
			// Set Current GameState.
			var cState = GameStateManager.CurrentStateName.ToString();

			// Check if Player left the expedition to prevent game crashing.
			if (cState != "Generating" && cState != "ReadyToStopElevatorRide" && cState != "StopElevatorRide" && cState != "ReadyToStartLevel" && cState != "InLevel")
			{
                Log.LogWarning($"Expedition Aborted, Closing Link Connection.");
				// Stop sending data to Mumble
				reportingTaskTimer.Change(Timeout.Infinite, Timeout.Infinite);

				// Start checking Gamestate again
				gameStateCheckTimer = new Timer(CheckGameState, null, System.TimeSpan.Zero, System.TimeSpan.FromSeconds(3));

				// Close Mumble Link Connection
				mumbleLink.Dispose();
				mumbleLink = null;

				isPlayerInLevel = false;

                return;
			}

			if (isPlayerInLevel == false)
			{
                // SendErrorLog($"Player is not in level yet! Holding...", true); //   --- VERY spammy, even for verbose lol.
                return;
            }
            
			// Added this to allow player to join mid game. EnterLevel Event fires before PlayerAgent is created, thus leading to a crash.
			// This should prevent that, and give it enough time to process the PlayerAgent.
			if (Player.PlayerManager.HasLocalPlayerAgent() == false) {
                SendErrorLog("No PlayerAgent.", true);
                return;
			}
            // Execute the code to get player variables and output them to the console
            PlayerAgent character = Player.PlayerManager.GetLocalPlayerAgent();
            var position = character.EyePosition - new Vector3(0, 1, 0);
			var ucam = character.FPSCamera;

			if (character != null && ucam != null && cState != null)
			{
				//   SendDebugLog($"Everything is set. (!= null).");
				if (mumbleLink == null)
				{
					SendDebugLog($"Initializing Load(). (mumbleLink == null).", false);
					Load();
				}

				mumblelib.Frame* frame = mumbleLink.FramePtr();

				if (ucam.Position != null)
				{
					frame->fCameraPosition[0] = ucam.Position.x;
					frame->fCameraPosition[1] = ucam.Position.y;
					frame->fCameraPosition[2] = ucam.Position.z;
				}

				if (ucam.Forward != null)
				{
					frame->fCameraFront[0] = ucam.Forward.x;
					frame->fCameraFront[1] = ucam.Forward.y;
					frame->fCameraFront[2] = ucam.Forward.z;
				}

				if (position != null && GameStateManager.CurrentStateName.ToString() == "InLevel")
				{
					frame->fAvatarPosition[0] = position.x;
					frame->fAvatarPosition[1] = position.y;
					frame->fAvatarPosition[2] = position.z;
				}
				else if (character.Forward == null || GameStateManager.CurrentStateName.ToString() != "InLevel") // If Player is null, use camera position instead.
				{
					//SendDebugLog($"Character is null, sending camera position instead. {ucam.Position}");
					frame->fAvatarPosition[0] = ucam.Position.x;
					frame->fAvatarPosition[1] = ucam.Position.y;
					frame->fAvatarPosition[2] = ucam.Position.z;
				}

				if (character.Forward != null && GameStateManager.CurrentStateName.ToString() == "InLevel")
				{
					// SendDebugLog($"Character is set. {character.Forward.x}, {character.Forward.y}, {character.Forward.z}");
					frame->fAvatarFront[0] = character.Forward.x;
					frame->fAvatarFront[1] = character.Forward.y;
					frame->fAvatarFront[2] = character.Forward.z;
				}
				else if (character.Forward == null || GameStateManager.CurrentStateName.ToString() != "InLevel") // If Player is null, use camera forward instead.
				{
					// SendDebugLog($"Character is null OR GSM is not InLevel, sending camera forward instead. {ucam.Forward.x}, {ucam.Forward.y}, {ucam.Forward.z}");
					frame->fAvatarFront[0] = ucam.Forward.x;
					frame->fAvatarFront[1] = ucam.Forward.y;
					frame->fAvatarFront[2] = ucam.Forward.z;
				}

				frame->uiTick++;
			}
			else
			{
				if (mumbleLink != null)
				{
					SendDebugLog($"Closing Link Connection.", false);
					mumbleLink.Dispose();
					mumbleLink = null;
                    isPlayerInLevel = false;
                    return;
				}
				Log.LogError($"An error has occurred.");
			}
		}


		public unsafe void FindTalkState()
		{
			// Set Current GameState.
			var cState = GameStateManager.CurrentStateName.ToString();

			Thread clientThread = new(ReadMemoryMappedFile);
			clientThread.Start();

            SendDebugLog($"Initiated TalkState!", false);

			if (cState != "Generating" && cState != "ReadyToStopElevatorRide" && cState != "StopElevatorRide" && cState != "ReadyToStartLevel" && cState != "InLevel")
			{
				SendDebugLog($"Expedition Aborted, Closing TalkState Connection.", false);
                isPlayerInLevel = false;

                // Stop this thread.
                clientThread.Join();

				return;
			}

			//ReadMemoryMappedFile();
			void ReadMemoryMappedFile()
			{
                while (isPlayerInLevel == false)
                {
                    SendErrorLog($"Player is not in level yet! Holding...", true);
					Thread.Sleep(5000); // Check every 5 seconds.
                }

                // Mid-Joiners Rejoyce.
                while (Player.PlayerManager.HasLocalPlayerAgent() == false)
                {
                    SendErrorLog("No PlayerAgent.", true);
                }
                PlayerAgent character = Player.PlayerManager.GetLocalPlayerAgent();
                bool sendStartOnce = false;
                bool sendStopOnce = false;
                HashSet<string> excludedStates = new HashSet<string> { "Generating", "ReadyToStopElevatorRide", "StopElevatorRide", "ReadyToStartLevel", "InLevel" };
                int intensity = OpenPAConfig.configIntensity.Value;
                const string memoryMappedFileName = "posaudio_mumlink";
                const int dataSize = 1024;
                SendDebugLog($"Initiated RMMF!", false);


				while (true)
				{
                    if (!excludedStates.Contains(GameStateManager.CurrentStateName.ToString()))
                    {
						break;
					}
                    using (var mmf = MemoryMappedFile.OpenExisting(memoryMappedFileName))
					{
						using (var accessor = mmf.CreateViewAccessor(0, dataSize))
						{
							byte[] dataBytes = new byte[dataSize];
							accessor.ReadArray(0, dataBytes, 0, dataSize);

							string receivedData = Encoding.UTF8.GetString(dataBytes).TrimEnd('\0');
							// SendDebugLog($"Received Data: {receivedData}");

							if (SNet.LocalPlayer.IsMaster == true) // Is declared as the host.
							{
								// HOST START
								if (receivedData == "Talking")
								{

									character.Noise = Agent.NoiseType.Walk;
                                    SendDebugLog($"Sending WALK type to localplayeragent.", true);
								}

							}
							else // Is declared as the client.
							{
								if (receivedData != "Talking") // Send stop signal
								{
									if (sendStopOnce == false) // hasn't been stopped yet.
									{
                                        SendDebugLog($"Sending stop signal for '{SteamManager.LocalPlayerName.ToString()}'.", true);
                                        NetworkAPI.InvokeEvent<ClientSendData>("Client_Status", new ClientSendData
                                        {
                                            clientSlot = Player.PlayerManager.GetLocalPlayerSlotIndex(),
                                            clientStatus = false, // False == Not Talking
                                        });

										sendStopOnce = true;
										sendStartOnce = false;
                                    }

                                } else if (receivedData == "Talking") // is currently talking
								{
									if (sendStartOnce == false) // hasn't been started yet.
									{
										SendDebugLog($"Sending talk signal for '{character.PlayerName}'!", true);
										NetworkAPI.InvokeEvent<ClientSendData>("Client_Status", new ClientSendData
										{
											clientSlot = Player.PlayerManager.GetLocalPlayerSlotIndex(),
											clientStatus = true, // True == Talking
										});

                                        sendStartOnce = true;
										sendStopOnce = false;
                                    }
                                }

                                sendStartOnce = false;

                            }
						}
					}
                    Thread.Sleep(intensity); // Sleep for 120 milliseconds.
									   // Adjust update frequency if CPU performance is bad.
				}
			}
		}

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct ClientSendData
        {
            public int clientSlot;
            public bool clientStatus;
        }


        public void HostSync()
        {
            SendDebugLog($"Checking HostSync...", true);
            if (NetworkAPI.IsEventRegistered("Client_Status") == true) { return; } // prevent opening more than one event handler.

            NetworkAPI.RegisterEvent<ClientSendData>("Client_Status", (senderId, packet) =>
            {
                if (SNet.LocalPlayer.IsMaster == true)
                {
                    // Run host sync code, this is fired when a client sends a message.
                    SendDebugLog($"Message received from {senderId}: {packet.clientSlot}, {packet.clientStatus}.", true);

					// run code here
					if (packet.clientStatus == true)
					{
                        PlayerStatus.PlayerStartedTalking(packet.clientSlot, true);
                    }
					else
					{
                        PlayerStatus.PlayerStoppedTalking(packet.clientSlot);
                    }

                }

                SendDebugLog($"Triggered NetworkEvent.", true);
            });
            SendDebugLog($"Registered NetworkEvent.", true);
        }

		public void PlayerStatusChangedHandler(object sender, PlayerStatusChangedEvent e)
		{
            int cSlot = e.PlayerID;
            PlayerAgent clientAgent = null;

            if (e.IsTalking)
			{
                SendDebugLog($"Player {e.PlayerID} started talking.", true);
                bool trygetresult = Player.PlayerManager.TryGetPlayerAgent(ref cSlot, out clientAgent);
                if (trygetresult)
                {
					clientAgent.Noise = Agent.NoiseType.Walk;
                }
            }
            else
            {
                SendDebugLog($"Player {e.PlayerID} stopped talking.", true);
            }
		}

        public interface LinkFileFactory
		{
			LinkFile Open();
		}

		public interface LinkFile : System.IDisposable
		{
			uint UIVersion { set; }
			void Tick();
			Vector3 CharacterPosition { set; }
			Vector3 CharacterForward { set; }
			Vector3 CharacterTop { set; }
			string Name { set; }
			Vector3 CameraPosition { set; }
			Vector3 CameraForward { set; }
			Vector3 CameraTop { set; }
			string ID { set; }
			string Context { set; }
			string Description { set; }
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