using UnityEngine;

namespace DeadAir
{
    public static class DeadAirClothing
    {
        public const int Shirt = 1, Jacket = 2, Trousers = 4, Shoes = 8, Underwear = 16;
        public const int All = Shirt | Jacket | Trousers | Shoes | Underwear;
        // -1 is serialized as the class default; zero deliberately removes every
        // garment. Appearance never changes collision, abilities or adult age.
        public static int Sanitize(int mask) => mask == -1 ? -1 : mask & All;
        public static int DefaultMask(string classId) => classId == "operator" || classId == "dispatcher" ? All & ~Jacket : All;
        public static int CasualMask(string classId) => Shirt | Trousers | Shoes | Underwear;
        public static int Resolve(PlayerState state) => state == null || state.clothingMask < 0 ? DefaultMask(state?.characterClass) : Sanitize(state.clothingMask);
        public static bool FirstPersonSleeves(int mask, string classId) => ((mask < 0 ? DefaultMask(classId) : Sanitize(mask)) & (Shirt | Jacket)) != 0;
        public static void Apply(GameObject avatar, int mask, string classId)
        {
            if (avatar == null) return;
            var appearance = avatar.GetComponent<DeadAirAppearance>();
            if (appearance == null) appearance = avatar.AddComponent<DeadAirAppearance>();
            appearance.Apply(mask < 0 ? DefaultMask(classId) : Sanitize(mask), classId);
        }
    }
}
