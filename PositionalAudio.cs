using BepInEx;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using System.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;
using MultiplayerBasicExample;
using static Il2CppSystem.Globalization.CultureInfo;
using SNetwork;

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

            if (GameStateManager.CurrentStateName.ToString() == "InLevel")
            {
                // await Task.Delay(1000);
                Log.LogInfo("Game is now in the 'InLevel' state.");

				// Run Mumble Setup
				mumbleLink = mumblelib.MumbleLinkFile.CreateOrOpen();
				mumblelib.Frame* frame = mumbleLink.FramePtr();
				frame->SetName("GTFO");
				frame->uiVersion = 2;
				string id = randomString(16);
				Log.LogInfo($"Setting Mumble ID to {id}");
				frame->SetID(id);
				Log.LogInfo($"Setting context to {cState}");
				frame->SetContext(cState);

				Log.LogInfo("Mumble Shared Memory Initialized");


				// Stop the game state check timer
				gameStateCheckTimer.Change(Timeout.Infinite, Timeout.Infinite);

                // Start the ReportingTask every second
                reportingTaskTimer = new Timer(FixedUpdated, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(12));
            }
            else
            {
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
			// Execute the code to get player variables and output them to the console
			var cState = GameStateManager.CurrentStateName.ToString();
			var character = Player.PlayerManager.GetLocalPlayerAgent();
            var position = character.EyePosition - new Vector3(0, 1, 0);
			// var rotation = character.transform.localEulerAngles - new Vector3(0, 1, 0);
			// var anglex = character.gameObject.transform.localRotation.x;
            // var angley = character.gameObject.transform.localRotation.y;
            // var anglez = character.gameObject.transform.localRotation.z;

			// Camera
			Transform camera = GameObject.Find("FPSCameraHolder_PlayerLocal(Clone)")?.transform;
			//var angle = Quaternion.LookRotation(Vector3.forward).eulerAngles;

			// Convert Vector3 components to strings
			string positionString = $"({position.x}, {position.y}, {position.z})";
			// string rotationString = $"({rotation.x}, {rotation.y}, {rotation.z})";
			//string angleString = $"({angle.x}, {angle.y}, {angle.z})";

			//   Log.LogInfo($"Player Position: {positionString}");
			// Log.LogInfo($"Player Angle: {rotationString}");

			if (character != null && camera != null && cState != null)
			{
				//   Log.LogInfo($"Everything is set. (!= null).");
				if (mumbleLink == null)
				{
					//   	Log.LogInfo($"Initializing Link(). (mumbleLink == null).");
					Load();
				}

				mumblelib.Frame* frame = mumbleLink.FramePtr();

				if (camera.localPosition != null)
				{
					//   	Log.LogInfo($"Sening Camera Position. (X: {camera.localPosition.x})");
					frame->fCameraPosition[0] = camera.localPosition.x;
					frame->fCameraPosition[1] = camera.localPosition.y;
					frame->fCameraPosition[2] = camera.localPosition.z;
				}

				if (camera.forward != null)
				{
					//   	Log.LogInfo($"Sening Camera Position. (fX: {character.Forward.x})");
					frame->fCameraFront[0] = character.Forward.x;
					frame->fCameraFront[1] = character.Forward.y;
					frame->fCameraFront[2] = character.Forward.z;
				}

				if (position != null)
				{
					//   	Log.LogInfo($"Sening Player Position. (X: {position.x})");
					frame->fAvatarPosition[0] = position.x;
					frame->fAvatarPosition[1] = position.y;
					frame->fAvatarPosition[2] = position.z;
				}

				if (character.Forward != null)
				{
					//   	Log.LogInfo($"Sening Player Position. (fX: {character.Forward.x})");
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
				}
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
