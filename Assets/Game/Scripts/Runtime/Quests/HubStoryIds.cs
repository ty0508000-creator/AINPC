public static class HubStoryIds
{
    public const string Arrival = "hub_arrival", Testimony = "hub_testimony";
    public static string Hunt(int number) => "hub_hunt_" + number;
    public static string Area(int number) => "hub_area_" + number;
    public static string Monster(int number) => "hub_enemy_" + number;
    public static string Scene(int number) => "HuntingGround" + number;
    public static string ReturnSpawn(int number) => "from_hunt_" + number;
    public const string Elder = "hub_elder", Hayeon = "hub_hayeon", Rescue = "hub_child_rescued";
    public const string Boss = "hub_ash_king", Ending = "hub_isle1_report", BossScene = "AshKingArena", BossReturn = "from_ash_king";
}
