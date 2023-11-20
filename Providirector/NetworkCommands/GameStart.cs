using UnityEngine.Networking;
using UnityEngine;
using RoR2;
namespace Providirector.NetworkCommands
{
	public class GameStart: MessageBase
	{
        public GameObject gameobject;
        public NetworkUser user => gameobject.GetComponent<NetworkUser>();

        public override void Deserialize(NetworkReader reader)
        {
            gameobject = reader.ReadGameObject();
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(gameobject);
        }
    }
}

