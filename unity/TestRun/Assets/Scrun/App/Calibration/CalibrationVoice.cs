using System.Collections.Generic;
using UnityEngine;

namespace Scrun.App
{
    // 보정 소리 안내(계획 0023 결정 3: 미리 녹음한 음성). 파일은 Resources/Audio/Calibration/ 아래 이름으로 둔다.
    //   prepare_2 … : 처음·도움 뒤 "러닝머신을 2 km/h로 맞추고 걸어 주세요"(속도마다 하나, 사다리 7개)
    //   next_4 …    : 단계 끝 3초 전 "곧 4 km/h로 올려요"
    //   go_4 …      : "지금 4 km/h로 올려 주세요"
    //   retry, help, stopped, resume, done : 속도 없는 안내
    // 파일이 아직 없으면 조용히 건너뛴다(화면 글자로 충분히 진행됨). 문구 원본은 CalibrationCopy.
    public static class CalibrationVoice
    {
        static AudioSource source;
        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        static readonly HashSet<string> missing = new HashSet<string>();

        public static string Key(CalCue cue, float kmh) => cue switch
        {
            CalCue.Prepare => $"prepare_{kmh:0.#}",
            CalCue.NextSoon => $"next_{kmh:0.#}",
            CalCue.Go => $"go_{kmh:0.#}",
            CalCue.Retry => "retry",
            CalCue.Help => "help",
            CalCue.Stopped => "stopped",
            CalCue.Resume => "resume",
            CalCue.Done => "done",
            _ => null,
        };

        public static void Play(CalCue cue, float kmh)
        {
            if (!Application.isPlaying) return;
            var key = Key(cue, kmh);
            if (key == null || missing.Contains(key)) return;
            if (!cache.TryGetValue(key, out var clip))
            {
                clip = Resources.Load<AudioClip>("Audio/Calibration/" + key);
                if (clip == null)
                {
                    missing.Add(key);
                    return;
                }
                cache[key] = clip;
            }
            if (source == null)
            {
                var go = new GameObject("[CalibrationVoice]") { hideFlags = HideFlags.HideInHierarchy };
                Object.DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
            }
            source.Stop(); // 앞 안내가 길어도 새 안내가 이김
            source.clip = clip;
            source.Play();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            source = null;
            cache.Clear();
            missing.Clear();
        }
    }
}
