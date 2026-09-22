using DnWModLoader.Config;

namespace Parkour
{
    public enum SprintMode
    {
        Hold,
        Toggle,
    }

    internal sealed class ParkourSettings
    {
        public readonly ConfigEntry<bool> Sprint;
        public readonly ConfigEntry<SprintMode> SprintMode;
        public readonly ConfigEntry<bool> DoubleJump;
        public readonly ConfigEntry<bool> WallRun;
        public readonly ConfigEntry<bool> WallJump;

        public ParkourSettings(ModConfig config)
        {
            config.DescribeSection("Parkour", "Parkour", "Sprint, double jump and wall runs!");
            Sprint = config.Bind("Parkour", "Sprint", true, "Enable sprinting");
            SprintMode = config.Bind("Parkour", "SprintMode", Parkour.SprintMode.Hold, "Sprint mode (toggle or hold)");
            DoubleJump = config.Bind("Parkour", "DoubleJump", true, "Enable double jump");
            WallRun = config.Bind("Parkour", "WallRun", true, "Enable wall run");
            WallJump = config.Bind("Parkour", "WallJump", true, "Enable wall jump");
        }
    }
}
