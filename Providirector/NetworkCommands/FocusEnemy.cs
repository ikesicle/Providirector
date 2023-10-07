using System;
using RoR2;
using UnityEngine.Networking;
namespace Providirector.NetworkCommands
{
    public class FocusEnemy : MessageBase
    {
        public CharacterMaster target;

        public override void Deserialize(NetworkReader reader)
        {
            var obj = reader.ReadGameObject();
            if (obj) target = obj.GetComponent<CharacterMaster>();
            else target = null;
        }

        public override void Serialize(NetworkWriter writer)
        {
            if (target) writer.Write(target.gameObject);
            else throw new NullReferenceException("target invalid");
        }
    }
}

