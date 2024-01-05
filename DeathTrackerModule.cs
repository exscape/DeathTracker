using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Celeste;
using Celeste.Mod;
using Monocle;

namespace CelesteDeathTracker
{
    public class DeathTrackerModule : EverestModule
    {
        public static DeathTrackerModule Module;
        private static DeathDisplay display = null;
        private static int DeathsSinceLevelLoad = 0;
        private static int DeathsSinceTransition = 0;
        private static float TimeSinceLevelLoad = 0f;
        private static float TimeSinceTransition = 0f;
        private static float LastUpdateTime = 0f;

        public DeathTrackerModule()
        {
            Module = this;
        }

        public override Type SettingsType => typeof(DeathTrackerSettings);
        public static DeathTrackerSettings Settings => (DeathTrackerSettings) Module._Settings;

        private static string FormatTime(float time)
        {
            int truncatedTime = (int)time;
            if (truncatedTime < 3600)
                return $"{(truncatedTime / 60):D2}:{(truncatedTime % 60):D2}";
            else
                return $"{truncatedTime / 3600}:{((truncatedTime % 3600) / 60):D2}:{((truncatedTime % 3600) % 60):D2}";
        }

        public static void UpdateDisplay(Level level)
        {
            var mode = (int)level.Session.Area.Mode;
            var stats = level.Session.OldStats.Modes[mode];

            display.SetDisplayText(new StringBuilder(Settings.DisplayFormat)
                .Replace("$TIME_LEVEL", FormatTime(TimeSinceLevelLoad))
                .Replace("$TIME_SCREEN", FormatTime(TimeSinceTransition))
                .Replace("$C", level.Session.Deaths.ToString())
                .Replace("$B", stats.SingleRunCompleted ? stats.BestDeaths.ToString() : "-")
                .Replace("$A", SaveData.Instance.Areas_Safe.First(a => a.ID_Safe == level.Session.Area.ID).Modes[mode].Deaths.ToString())
                .Replace("$T", SaveData.Instance.TotalDeaths.ToString())
                .Replace("$L", DeathsSinceLevelLoad.ToString())
                .Replace("$S", DeathsSinceTransition.ToString())
                .ToString());
        }

        public override void Load()
        {
            Level level = null;

            On.Celeste.LevelLoader.StartLevel += (orig, self) =>
            {
                level = self.Level;
                level.Add(display = new DeathDisplay(level));
                DeathsSinceLevelLoad = 0;
                DeathsSinceTransition = 0;
                TimeSinceLevelLoad = 0f;
                TimeSinceTransition = 0f;
                orig(self);
            };
            
            Everest.Events.Player.OnDie += player =>
            {
                var sessionDeaths = level.Session.Deaths;
                var stats = level.Session.OldStats.Modes[(int)level.Session.Area.Mode];
                DeathsSinceLevelLoad++;
                DeathsSinceTransition++;

                if (Settings.AutoRestartChapter && stats.SingleRunCompleted && sessionDeaths > 0 &&
                    sessionDeaths >= stats.BestDeaths)
                {
                    Engine.TimeRate = 1f;
                    level.Session.InArea = false;
                    Audio.SetMusic(null);
                    Audio.BusStopAll("bus:/gameplay_sfx", true);
                    level.DoScreenWipe(false, () => Engine.Scene = new LevelExit(LevelExit.Mode.GoldenBerryRestart, level.Session));

                    foreach (var component in level.Tracker.GetComponents<LevelEndingHook>())
                    {
                        ((LevelEndingHook)component).OnEnd?.Invoke();
                    }
                }
            };

            Everest.Events.Player.OnSpawn += player =>
            {
                UpdateDisplay(level);
            };

            Everest.Events.Level.OnTransitionTo += (lvl, next, direction) =>
            {
                DeathsSinceTransition = 0;
                TimeSinceTransition = 0f;
                UpdateDisplay(lvl);
            };

            On.Celeste.Level.UpdateTime += (orig, self) =>
            {
                orig(self);
                if (!self.InCredits && self.Session.Area.ID != 8 && !self.TimerStopped)
                {
                    // This condition is the exact condition used in Level.UpdateTime, to ensure we measure the same amount of time
                    // However, we DON'T use the exact calculation, as the game introduces a 2% error (17 / (16 + 2/3) which is exactly 2%) which we don't want.
                    TimeSinceTransition += Engine.RawDeltaTime;
                    TimeSinceLevelLoad += Engine.RawDeltaTime;
                }

                if (TimeSinceTransition - LastUpdateTime > 1f)
                {
                    LastUpdateTime = TimeSinceTransition;
                    UpdateDisplay(level);
                }
            };
        }

        public override void Unload()
        {
        }
    }
}
