using GTFO.API;
using Random = System.Random;

namespace OpenUtils
{
    class OPAUtils
    {
        private static Random random = new Random();

        public static string RandomString(int len)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Range(0, len)
                .Select(_ => chars[random.Next(chars.Length)]).ToArray());
        }
    }

    public class NoiseAgentHandler // This is for the Client to send the SteamMessage to the Host.
                                   // It automatically sends each status update only once.
    {
        private enum State
        {
            Idle,
            Talking,
            Passive
        }

        private State currentState = State.Idle;

        public void ClientNoiseAgent(string receivedData)
        {
            switch (receivedData)
            {
                case "Talking":
                    if (currentState != State.Talking)
                    {
                        HandleTalking();
                        currentState = State.Talking;
                    }
                    break;

                case "Passive":
                    if (currentState == State.Talking)
                    {
                        HandlePassive();
                        currentState = State.Passive;
                    }
                    break;

                default:
                    break;
            }
        }

        private void HandleTalking()
        {
            NetworkAPI.InvokeEvent("Client_Status", new OpenPA3.Plugin.ClientSendData
            {
                clientSlot = Player.PlayerManager.GetLocalPlayerSlotIndex(),
                clientStatus = true, // True == Talking
            });

            Console.WriteLine("Handled Talking");
        }

        private void HandlePassive()
        {
            // Code to run once when transitioning to "Passive"
            NetworkAPI.InvokeEvent("Client_Status", new OpenPA3.Plugin.ClientSendData
            {
                clientSlot = Player.PlayerManager.GetLocalPlayerSlotIndex(),
                clientStatus = false, // False == Not Talking
            });

            Console.WriteLine("Handled Passive");
        }
    }
}
