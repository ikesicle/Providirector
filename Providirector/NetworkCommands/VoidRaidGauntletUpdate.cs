using UnityEngine.Networking;
using UnityEngine;
using RoR2;
namespace Providirector.NetworkCommands
{
	public class VoidRaidGauntletUpdate: MessageBase
	{
        public NetworkInstanceId nid;
        public Vector3 position;

        public override void Deserialize(NetworkReader reader)
        {
            position = reader.ReadVector3();
            nid = new NetworkInstanceId(reader.ReadUInt32());
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(position);
            writer.Write(nid.Value);
        }
    }
}

