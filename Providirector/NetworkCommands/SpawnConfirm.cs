using System;
using ProvidirectorGame;
using UnityEngine;
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
            var obj = reader.ReadGameObject();
            if (obj) spawned = obj.GetComponent<CharacterMaster>();
            else spawned = null;
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(cost);
            if (spawned) writer.Write(spawned.gameObject);
            else throw new NullReferenceException("invalid spawned");
        }
    }
}

