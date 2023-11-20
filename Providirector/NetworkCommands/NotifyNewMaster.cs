using RoR2;
using UnityEngine.Networking;
namespace Providirector.NetworkCommands
{
	public class NotifyNewMaster: MessageBase
	{
        public CharacterMaster target;

        public override void Deserialize(NetworkReader reader)
        {
            target = Util.FindNetworkObject(reader.ReadNetworkId()).GetComponent<CharacterMaster>();
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(target.netId);
        }
    }
}

