using UnityEngine.Networking;
using ProvidirectorGame;
namespace Providirector.NetworkCommands
{
	public class SyncDirector: MessageBase
	{
        public bool serverData;
        public bool snapToNearestNode;
        public float creditInit;
        public float creditGain;
        public int walletInit;
        public int walletGain;
        public int spawnCap;

        public SyncDirector() { }

        public SyncDirector(bool serverData)
        {
            this.serverData = serverData;
            snapToNearestNode = DirectorState.snapToNearestNode;
            creditInit = DirectorState.baseCreditGain;
            creditGain = DirectorState.creditGainPerLevel;
            walletInit = (int)DirectorState.baseWalletSize;
            walletGain = (int)DirectorState.walletGainPerLevel;
            spawnCap = (int)DirectorState.directorSelfSpawnCap;
        }

        public override void Deserialize(NetworkReader reader)
        {
            serverData = reader.ReadBoolean();
            snapToNearestNode = reader.ReadBoolean();
            creditInit = reader.ReadSingle();
            creditGain = reader.ReadSingle();
            walletInit = reader.ReadInt32();
            walletGain = reader.ReadInt32();
            spawnCap = reader.ReadInt32();
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(serverData);
            writer.Write(snapToNearestNode);
            writer.Write(creditInit);
            writer.Write(creditGain);
            writer.Write(walletInit);
            writer.Write(walletGain);
            writer.Write(spawnCap);
        }
    }
}

