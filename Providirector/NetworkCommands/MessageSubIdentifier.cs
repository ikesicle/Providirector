using UnityEngine.Networking;
namespace Providirector.NetworkCommands
{
	public enum MessageType: int
	{
		Invalid = -1,
		Handshake = 0,
		HandshakeResponse = 1,
		SpawnEnemy = 2,
		FocusEnemy = 3,
		Burst = 4,
		GameStart = 5,
		ModeUpdate = 6,
		FPUpdate = 7,
		DirectorSync = 8,
		MovePosition = 9,
		VoidFieldDirectorSync = 10,
		NotifyNewMaster = 11,
		RequestBodyResync = 12,
		VoidRaidOnDeath = 13,
		FogSafeZone = 14,
		CachedCredits = 15
	}
	public class MessageSubIdentifier: MessageBase
	{
		public MessageType type = MessageType.Invalid;
		public uint returnValue = 1;
		public bool booleanValue => returnValue != 0;

        public override void Deserialize(NetworkReader reader)
        {
			type = (MessageType)reader.ReadInt32();
			returnValue = reader.ReadUInt32();
        }

        public override void Serialize(NetworkWriter writer)
        {
			writer.Write((int)type);
			writer.Write(returnValue);
        }
    }
}

