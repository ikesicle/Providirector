using UnityEngine.Networking;
using UnityEngine;
using RoR2;
using ProvidirectorGame;
namespace Providirector.NetworkCommands
{
	public class CardSync: MessageBase
	{
        public SpawnCardDisplayData[] cardDisplayDatas;

        public override void Deserialize(NetworkReader reader)
        {
            int count = reader.ReadInt32();
            cardDisplayDatas = new SpawnCardDisplayData[count];
            for (int i = 0; i < count; i++) cardDisplayDatas[i] = reader.ReadMessage<SpawnCardDisplayData>();
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(cardDisplayDatas.Length);
            foreach (SpawnCardDisplayData card in cardDisplayDatas)
            {
                writer.Write(card);
            }
        }
    }
}

