using PulsarModLoader;

namespace CapBot
{
    public class Mod : PulsarMod
    {
        public override string Version => "1.0.0";

        public override string Author => "pokegustavo";

        public override string ShortDescription => "Adds a bot as the captain";

        public override string Name => "CapBot";

        public override string HarmonyIdentifier()
        {
            return "pokegustavo.CapBot";
        }
    }
}
