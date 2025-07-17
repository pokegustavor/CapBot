﻿using PulsarModLoader;
[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo("Assembly-CSharp")]
namespace CapBot
{
    public class Mod : PulsarMod
    {
        public override string Version => "Alpha 1.2";

        public override string Author => "pokegustavo";

        public override string ShortDescription => "Adds a bot as the captain";

        public override string Name => "CapBot";

        public override string HarmonyIdentifier()
        {
            return "pokegustavo.CapBot";
        }
    }
}
