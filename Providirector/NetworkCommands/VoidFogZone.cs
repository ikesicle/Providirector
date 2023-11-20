using UnityEngine.Networking;
using UnityEngine;
using RoR2;
namespace Providirector.NetworkCommands
{
	public class VoidFogZone: MessageBase
	{
        public BaseZoneBehavior zone;

        public override void Deserialize(NetworkReader reader)
        {
            zone = reader.ReadGameObject()?.GetComponent<BaseZoneBehavior>();
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(zone.gameObject);
        }
    }
}

