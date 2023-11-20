using ProvidirectorGame;
using UnityEngine;
using UnityEngine.Networking;
namespace Providirector.NetworkCommands
{
	public class SpawnEnemy: MessageBase
	{
        public int slotIndex;
        public EliteTierIndex eliteClassIndex;
        public Vector3 position;
        public Quaternion rotation;

        public override void Deserialize(NetworkReader reader)
        {
            slotIndex = reader.ReadInt32();
            eliteClassIndex = (EliteTierIndex)reader.ReadInt32();
            position = reader.ReadVector3();
            rotation = reader.ReadQuaternion();
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(slotIndex);
            writer.Write((int)eliteClassIndex);
            writer.Write(position);
            writer.Write(rotation);
        }
    }
}

