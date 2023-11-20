using UnityEngine.Networking;
using UnityEngine;
using RoR2;
namespace Providirector.NetworkCommands
{
	public class MovePosition: MessageBase
	{
        public string intendedSceneName;
        public Vector3 position;

        public override void Deserialize(NetworkReader reader)
        {
            position = reader.ReadVector3();
            intendedSceneName = reader.ReadString();
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(position);
            writer.Write(intendedSceneName);
        }
    }
}

