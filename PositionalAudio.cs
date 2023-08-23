using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace PositionalAudio
{
	[BepInPlugin("net.devante.gtfo.positionalaudio", "PositionalAudio", "1.0.0")]
	public class Plugin : BasePlugin
	{
		mumblelib.MumbleLinkFile mumbleLink;
		private Timer gameStateCheckTimer;
		private Timer reportingTaskTimer;

		public override void Load()
		{
			// Plugin startup logic
			Log.LogInfo($"Plugin is loaded!");

			// Set up a timer to periodically check the game state every 5 seconds
			gameStateCheckTimer = new Timer(CheckGameState, null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
		}

		private unsafe void CheckGameState(object state)
		{
			var cState = GameStateManager.CurrentStateName.ToString();

			if (cState == "Generating" || cState == "InLevel")
			{
				// await Task.Delay(1000);
				Log.LogInfo("Game is now in the 'Generating' OR 'InLevel' state.");

				// Run Mumble Setup
				mumbleLink = mumblelib.MumbleLinkFile.CreateOrOpen();
				mumblelib.Frame* frame = mumbleLink.FramePtr();
				frame->SetName("GTFO");
				frame->uiVersion = 2;
				string id = randomString(16);
				Log.LogInfo($"Setting Mumble ID to {id}");
				frame->SetID(id);
				Log.LogInfo($"Setting context to InLevel");
				frame->SetContext("InLevel");

				Log.LogInfo("Mumble Shared Memory Initialized");


				// Stop the game state check timer
				gameStateCheckTimer.Change(Timeout.Infinite, Timeout.Infinite);

				// Start the ReportingTask every second
				reportingTaskTimer = new Timer(FixedUpdated, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(12));
			}
			else
			{
				// Important for debugging in development builds.
				Log.LogInfo($"Currently not in level. Reattempting.. ({cState})");
			}
		}

		private static System.Random random = new System.Random();
		private string randomString(int len)
		{
			const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
			return new string(Enumerable.Repeat(chars, len)
				.Select(s => s[random.Next(s.Length)]).ToArray());
		}


		private unsafe void FixedUpdated(object state)
		{
			// Set Current GameState.
			var cState = GameStateManager.CurrentStateName.ToString();

			// Check if Player left the expedition to prevent game crashing.
			if (cState != "Generating" && cState != "ReadyToStopElevatorRide" && cState != "StopElevatorRide" && cState != "ReadyToStartLevel" && cState != "InLevel")
			{
				Log.LogInfo($"Expedition Aborted, Closing Link Connection.");
				// Stop sending data to Mumble
				reportingTaskTimer.Change(Timeout.Infinite, Timeout.Infinite);

				// Start checking Gamestate again
				gameStateCheckTimer = new Timer(CheckGameState, null, TimeSpan.Zero, TimeSpan.FromSeconds(3));

				// Close Mumble Link Connection
				mumbleLink.Dispose();
				mumbleLink = null;

				return;
			}

			// Execute the code to get player variables and output them to the console
			var character = Player.PlayerManager.GetLocalPlayerAgent();
			var position = character.EyePosition - new Vector3(0, 1, 0);
            var ucam = character.FPSCamera;

            // OldCamera
            //    Transform camera = GameObject.Find("FPSCameraHolder_PlayerLocal(Clone)")?.transform;

			// Convert Vector3 components to strings - Only needed if debug outputs are uncommented.
			//    string positionString = $"({position.x}, {position.y}, {position.z})";
			//    Log.LogInfo($"Player Position: {positionString}");

			if (character != null && ucam != null && cState != null)
			{
				//   Log.LogInfo($"Everything is set. (!= null).");
				if (mumbleLink == null)
				{
					Log.LogInfo($"Initializing Load(). (mumbleLink == null).");
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

				if (position != null)
				{
					frame->fAvatarPosition[0] = position.x;
					frame->fAvatarPosition[1] = position.y;
					frame->fAvatarPosition[2] = position.z;
				}

				if (character.Forward != null)
				{
					frame->fAvatarFront[0] = character.Forward.x;
					frame->fAvatarFront[1] = character.Forward.y;
					frame->fAvatarFront[2] = character.Forward.z;
				}

				frame->uiTick++;
			}
			else
			{
				if (mumbleLink != null)
				{
					Log.LogInfo($"Closing Link Connection.");
					mumbleLink.Dispose();
					mumbleLink = null;
					return;
				}
                Log.LogInfo($"An error has occurred.");
            }
		}
		public interface LinkFileFactory
		{
			LinkFile Open();
		}

		public interface LinkFile : IDisposable
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
}
