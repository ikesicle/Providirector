using RoR2;
using UnityEngine.Networking;
namespace Providirector.NetworkCommands
{
	public class SpawnConfirm: MessageBase
	{
        public float cost;
        public CharacterMaster spawned;

        public override void Deserialize(NetworkReader reader)
        {
            cost = reader.ReadSingle();
            bool spawnedExists = reader.ReadBoolean();
            if (spawnedExists) spawned = reader.ReadGameObject().GetComponent<CharacterMaster>();
            else spawned = null;
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(cost);
            if (spawned)
            {
                writer.Write(true);
                writer.Write(spawned.gameObject);
            }
            else writer.Write(false);
        }
    }
}

