using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DeadAir.Editor
{
    public static class DeadAirBreakerValidation
    {
        static readonly MethodInfo Attempt = typeof(DeadAirRules).GetMethod("TrySabotageBreaker", BindingFlags.NonPublic | BindingFlags.Instance);
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException("Breaker: " + message); }
        static bool Try(DeadAirRules rules, int roll = 0) => (bool)Attempt.Invoke(rules, new object[] { roll });

        [MenuItem("Dead Air/Validate Breaker Protection")]
        public static void Validate()
        {
            var world = WorldData.Load();
            var rules = new DeadAirRules(world, "host", "Breaker verifier", "engineer", true, false);
            rules.Start("host");
            rules.State.power = rules.State.quest.power = true;
            var breaker = world.Objective("breaker");
            var enemy = rules.State.enemy;
            enemy.active = true; enemy.x = breaker.x; enemy.z = breaker.z; enemy.y = -4.2f;
            rules.ExecuteCommand("host", "breaker");
            for (int i = 0; i < 120; i++)
            {
                rules.State.elapsed = i;
                Require(!Try(rules) && rules.State.breakerOn, "Even a winning roll cannot interrupt protected light.");
            }
            rules.State.elapsed = 120;
            enemy.x += 10;
            Require(!Try(rules), "Remote sabotage must be impossible.");
            rules.State.elapsed = 180; enemy.x = breaker.x; enemy.heardUntil = 190;
            Require(!Try(rules), "Investigating noise takes priority over sabotage.");
            rules.State.elapsed = 240; enemy.stunnedUntil = 250;
            Require(!Try(rules), "Stunned monsters cannot sabotage.");
            rules.State.elapsed = 300;
            Require(!Try(rules, 10), "Only rolls below ten succeed.");
            rules.State.elapsed = 360;
            Require(Try(rules) && !rules.State.breakerOn, "Eligible monster at the switch must cut power.");
            rules.ExecuteCommand("host", "breaker");
            rules.State.elapsed = 479;
            Require(!Try(rules) && rules.State.breakerOn, "Restoring the switch grants a fresh full protection interval.");
            rules.State.elapsed = 480;
            Require(Try(rules), "Second eligible sabotage is allowed.");
            rules.ExecuteCommand("host", "breaker");
            for (int i = 0; i < 100; i++)
            {
                rules.State.elapsed = 600 + i * 60;
                Require(!Try(rules) && rules.State.breakerOn, "Round cap prevents repeated cuts even with 100 winning rolls.");
            }
            rules.State.phase = "lost"; rules.Restart("host");
            rules.State.power = true; rules.ExecuteCommand("host", "breaker");
            enemy = rules.State.enemy; enemy.active = true; enemy.x = breaker.x; enemy.y = -4.2f; enemy.z = breaker.z;
            rules.State.elapsed = 120;
            Require(Try(rules), "Restart resets the sabotage allowance.");
            Debug.Log("DEAD AIR BREAKER PASSED: 120s protection, physical reach, patrol/stun guards, 10% threshold, two-cut cap, 100 repeated winning rolls and restart reset.");
        }
    }
}
