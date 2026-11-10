using System;
using System.Collections.Generic;

namespace Scrun.App
{
    // 마지막으로 고른 목표와 "다시 보지 않기"를 폰에 남긴다(계정과 상관없는 폰 설정. 에디터·기기 모두 PlayerPrefs)
    public sealed class RunSetupPrefs
    {
        const string KindKey = "scrun.run.goalKind";
        const string DistanceKey = "scrun.run.goalDistance";
        const string TimeKey = "scrun.run.goalTime";
        const string SkipManualKey = "scrun.run.skipManual";

        readonly IPrefs prefs;

        public RunSetupPrefs(IPrefs prefs) => this.prefs = prefs ?? throw new ArgumentNullException(nameof(prefs));

        public static RunSetupPrefs Device() => new RunSetupPrefs(new PlayerPrefsStore());

        // 처음엔 거리 3 km(Figma), 그다음부터는 마지막에 고른 종류와 값
        public RunGoal LastGoal() => LastGoal(prefs.GetInt(KindKey, 0) == 1 ? GoalKind.Time : GoalKind.Distance);

        // 그 종류로 마지막에 고른 값(탭을 바꿨을 때)
        public RunGoal LastGoal(GoalKind kind)
        {
            var key = kind == GoalKind.Distance ? DistanceKey : TimeKey;
            return new RunGoal(kind, prefs.GetInt(key, RunGoal.Default(kind).Units));
        }

        public void Remember(RunGoal goal)
        {
            prefs.SetInt(KindKey, goal.Kind == GoalKind.Time ? 1 : 0);
            prefs.SetInt(goal.Kind == GoalKind.Distance ? DistanceKey : TimeKey, goal.Units);
            prefs.Save();
        }

        public bool SkipManual
        {
            get => prefs.GetInt(SkipManualKey, 0) == 1;
            set { prefs.SetInt(SkipManualKey, value ? 1 : 0); prefs.Save(); }
        }
    }

    public interface IPrefs
    {
        int GetInt(string key, int fallback);
        void SetInt(string key, int value);
        void Save();
    }

    public sealed class PlayerPrefsStore : IPrefs
    {
        public int GetInt(string key, int fallback) => UnityEngine.PlayerPrefs.GetInt(key, fallback);
        public void SetInt(string key, int value) => UnityEngine.PlayerPrefs.SetInt(key, value);
        public void Save() => UnityEngine.PlayerPrefs.Save();
    }

    public sealed class MemoryPrefs : IPrefs
    {
        readonly Dictionary<string, int> values = new Dictionary<string, int>();
        public int GetInt(string key, int fallback) => values.TryGetValue(key, out var v) ? v : fallback;
        public void SetInt(string key, int value) => values[key] = value;
        public void Save() { }
    }
}
