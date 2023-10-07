using System;
using RoR2;
using UnityEngine.Networking;
namespace Providirector.NetworkCommands
{
	public enum MessageType: int
	{
		Invalid = -1,
		Handshake = 0,
		SpawnEnemy = 1,
		FocusEnemy = 2,
		Burst = 3,
		GameStart = 4,
		ModeUpdate = 5,
		FPUpdate = 6
	}
	public class MessageSubIdentifier: MessageBase
	{
		public MessageType type = MessageType.Invalid;
		public float returnValue = -1;
		public bool booleanValue => returnValue >= 0;

        public override void Deserialize(NetworkReader reader)
        {
			type = (MessageType)reader.ReadInt32();
			returnValue = reader.ReadSingle();
        }

        public override void Serialize(NetworkWriter writer)
        {
			writer.Write((int)type);
			writer.Write(returnValue);
        }
    }
}

