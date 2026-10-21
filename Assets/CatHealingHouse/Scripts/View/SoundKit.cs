using System.Collections.Generic;
using UnityEngine;

namespace CatHealingHouse.View
{
    /// <summary>
    /// 코드로 만든 임시 소리 (오디오 파일 없이 사인파·잡음으로 합성).
    /// 효과음: pop(버튼), click(작은 버튼·탭), coin(트레이 수확), heart(하트), pickup(털뭉치 줍기),
    /// open(창 열기), purr(골골송), meow(야옹), fanfare(영입·레벨업).
    /// 배경음: 오르골 느낌의 잔잔한 5음계 멜로디 32초 반복.
    /// 끄기/켜기는 PlayerPrefs 에 기억한다. 나중에 진짜 소리 파일로 바꿔 끼우면 된다.
    /// </summary>
    public sealed class SoundKit : MonoBehaviour
    {
        public static SoundKit I { get; private set; }
        private const int Rate = 22050;
        private AudioSource _sfx, _music;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private float _lastPurr;

        public bool Muted { get; private set; }

        public static void Play(string id, float volume = 1f, float pitch = 1f)
        {
            if (I == null || I.Muted || !I._clips.TryGetValue(id, out var clip)) return;
            if (id == "purr") { if (Time.time - I._lastPurr < 1.2f) return; I._lastPurr = Time.time; }
            I._sfx.pitch = pitch;
            I._sfx.PlayOneShot(clip, volume);
        }

        private void Awake()
        {
            I = this;
            _sfx = gameObject.AddComponent<AudioSource>();
            _sfx.playOnAwake = false;
            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = true;
            _music.volume = 0.32f;
            Muted = PlayerPrefs.GetInt("chh_mute", 0) == 1;
            Build();
            _music.clip = Music();
            if (!Muted) _music.Play();
        }

        public void SetMuted(bool m)
        {
            Muted = m;
            PlayerPrefs.SetInt("chh_mute", m ? 1 : 0);
            if (m) _music.Pause(); else if (_music.isPlaying) { } else _music.Play();
        }

        // ───────────────────────── 합성 ─────────────────────────

        private static AudioClip Clip(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        /// <summary>오르골·벨 같은 음: 사인 + 2배음, 빠른 어택, 지수 감쇠.</summary>
        private static void Bell(float[] buf, float start, float freq, float dur, float vol, float decay = 6f, float bright = 0.35f)
        {
            int s = (int)(start * Rate), n = (int)(dur * Rate);
            for (int i = 0; i < n && s + i < buf.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t / 0.004f) * Mathf.Exp(-decay * t);
                float w = Mathf.Sin(2 * Mathf.PI * freq * t) + bright * Mathf.Sin(2 * Mathf.PI * freq * 2f * t) + 0.12f * Mathf.Sin(2 * Mathf.PI * freq * 3f * t);
                buf[s + i] += w * env * vol;
            }
        }

        private static void Sweep(float[] buf, float start, float f0, float f1, float dur, float vol)
        {
            int s = (int)(start * Rate), n = (int)(dur * Rate);
            float phase = 0;
            for (int i = 0; i < n && s + i < buf.Length; i++)
            {
                float k = i / (float)n;
                float f = Mathf.Lerp(f0, f1, k);
                phase += 2 * Mathf.PI * f / Rate;
                float env = Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.2f)) * (1f - k);
                buf[s + i] += Mathf.Sin(phase) * env * vol;
            }
        }

        private static float N(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        private void Build()
        {
            var rng = new System.Random(7);

            var pop = new float[(int)(0.12f * Rate)];
            Sweep(pop, 0, 380, 920, 0.11f, 0.5f);
            _clips["pop"] = Clip("pop", pop);

            var click = new float[(int)(0.07f * Rate)];
            Bell(click, 0, 1320, 0.07f, 0.28f, 40f, 0.1f);
            _clips["click"] = Clip("click", click);

            var coin = new float[(int)(0.42f * Rate)];
            Bell(coin, 0, N(84), 0.2f, 0.32f, 14f);
            Bell(coin, 0.07f, N(91), 0.35f, 0.32f, 9f);
            _clips["coin"] = Clip("coin", coin);

            var heart = new float[(int)(0.6f * Rate)];
            int[] arp = { 84, 88, 91, 96 };
            for (int i = 0; i < arp.Length; i++) Bell(heart, i * 0.06f, N(arp[i]), 0.4f, 0.22f, 8f);
            _clips["heart"] = Clip("heart", heart);

            var pick = new float[(int)(0.18f * Rate)];
            Sweep(pick, 0, 660, 1400, 0.16f, 0.32f);
            _clips["pickup"] = Clip("pickup", pick);

            var open = new float[(int)(0.25f * Rate)];
            for (int i = 0; i < open.Length; i++)
            {
                float k = i / (float)open.Length;
                open[i] = ((float)rng.NextDouble() * 2 - 1) * 0.06f * Mathf.Sin(Mathf.PI * k);
            }
            Smooth(open, 0.85f);
            Sweep(open, 0.02f, 520, 780, 0.2f, 0.18f);
            _clips["open"] = Clip("open", open);

            // 골골송: 낮은 잡음을 1초에 25번 정도 출렁이게
            var purr = new float[(int)(1.4f * Rate)];
            for (int i = 0; i < purr.Length; i++) purr[i] = (float)rng.NextDouble() * 2 - 1;
            Smooth(purr, 0.985f);
            for (int i = 0; i < purr.Length; i++)
            {
                float t = i / (float)Rate;
                float am = 0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 24f * t);
                float env = Mathf.Min(1f, t / 0.15f) * Mathf.Min(1f, (1.4f - t) / 0.3f);
                purr[i] *= 9f * am * am * env;
            }
            _clips["purr"] = Clip("purr", purr);

            // 야옹: 톱니파 높낮이를 올렸다 내리고 부드럽게
            var meow = new float[(int)(0.55f * Rate)];
            float ph = 0;
            for (int i = 0; i < meow.Length; i++)
            {
                float k = i / (float)meow.Length;
                float f = k < 0.35f ? Mathf.Lerp(520, 760, k / 0.35f) : Mathf.Lerp(760, 430, (k - 0.35f) / 0.65f);
                ph += f / Rate;
                float saw = (ph % 1f) * 2f - 1f;
                float env = Mathf.Min(1f, k / 0.08f) * Mathf.Pow(1f - k, 1.4f);
                meow[i] = saw * env * 0.22f;
            }
            Smooth(meow, 0.6f);
            Smooth(meow, 0.6f);
            _clips["meow"] = Clip("meow", meow);

            var fan = new float[(int)(1.1f * Rate)];
            int[] notes = { 72, 76, 79, 84 };
            for (int i = 0; i < notes.Length; i++) Bell(fan, i * 0.1f, N(notes[i]), 0.8f, 0.22f, 4f);
            Bell(fan, 0.45f, N(88), 0.6f, 0.16f, 4f);
            _clips["fanfare"] = Clip("fanfare", fan);
        }

        /// <summary>한 극 저역 통과 (값이 클수록 더 뭉툭하게).</summary>
        private static void Smooth(float[] b, float k)
        {
            float y = 0;
            for (int i = 0; i < b.Length; i++) { y = k * y + (1 - k) * b[i]; b[i] = y; }
        }

        /// <summary>배경음: 다장조 5음계 오르골 멜로디 + 느린 베이스. 32초, 끝이 처음과 자연스럽게 이어진다.</summary>
        private static AudioClip Music()
        {
            const float len = 32f;
            var buf = new float[(int)(len * Rate)];
            var rng = new System.Random(2026);
            int[] scale = { 72, 74, 76, 79, 81, 84, 86, 88 }; // C5 D5 E5 G5 A5 C6 D6 E6
            int[] bass = { 48, 53, 55, 45 };                  // C3 F3 G3 A2
            float step = 0.5f;
            int idx = 2;
            for (float t = 0; t < len - 1.2f; t += step)
            {
                // 쉼표를 섞고, 가까운 음으로 걷듯이
                if (rng.NextDouble() < 0.28) continue;
                idx = Mathf.Clamp(idx + rng.Next(-2, 3), 0, scale.Length - 1);
                Bell(buf, t, N(scale[idx]), 1.6f, 0.09f, 2.6f, 0.25f);
                if (rng.NextDouble() < 0.15) Bell(buf, t + step * 0.5f, N(scale[Mathf.Clamp(idx + 2, 0, scale.Length - 1)]), 1.2f, 0.05f, 3f);
            }
            for (int bar = 0; bar < 16; bar++)
            {
                float t = bar * 2f;
                int b = bass[bar % bass.Length];
                Bell(buf, t, N(b), 1.9f, 0.07f, 1.4f, 0.1f);
                Bell(buf, t, N(b + 7), 1.9f, 0.035f, 1.4f, 0.05f);
            }
            // 맨 끝 0.3초는 서서히 줄여서 반복할 때 딸깍 소리가 안 나게
            int fade = (int)(0.3f * Rate);
            for (int i = 0; i < fade; i++) buf[buf.Length - 1 - i] *= i / (float)fade;
            return Clip("music", buf);
        }
    }
}
