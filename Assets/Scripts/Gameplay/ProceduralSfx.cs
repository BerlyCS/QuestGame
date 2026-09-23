using UnityEngine;

/// <summary>
/// Sound effects synthesized in code (no imported audio, matching this
/// project's no-external-assets rule). Clips are built once and cached.
/// Without haptics, every grab / burn / footstep has to be heard to exist.
/// </summary>
public static class ProceduralSfx
{
    const int k_SampleRate = 44100;

    static AudioClip s_WoodGrab, s_FireWhoosh, s_EnemyHit, s_BirdSong, s_SinisterLaugh, s_TreasureLand, s_CoronadoScream, s_CoronadoBreath;

    /// <summary>Dull wooden knock: the log settling into the hand.</summary>
    public static AudioClip WoodGrab => s_WoodGrab ??= Build("WoodGrab", 0.14f, (t, r) =>
        Mathf.Sin(2f * Mathf.PI * 130f * t) * 0.7f * Mathf.Exp(-t * 32f) + Noise(r) * 0.35f * Mathf.Exp(-t * 90f));

    /// <summary>Wood catching fire: a rising whoosh that decays into crackling.</summary>
    public static AudioClip FireWhoosh => s_FireWhoosh ??= BuildWhoosh();

    /// <summary>Dry bone crack with a low thump: an ember landing on an enemy.</summary>
    public static AudioClip EnemyHit => s_EnemyHit ??= Build("EnemyHit", 0.22f, (t, r) =>
        Noise(r) * 0.8f * Mathf.Exp(-t * 55f) + Mathf.Sin(2f * Mathf.PI * 95f * t) * 0.7f * Mathf.Exp(-t * 20f));

    /// <summary>Victory: a calm dawn chorus - birds trilling and chirping over a soft breeze (~9 s).</summary>
    public static AudioClip BirdSong => s_BirdSong ??= BuildBirdSong();

    /// <summary>Defeat: a low, slowed-down villain laugh, "ha... ha... ha-ha-ha" (~3.6 s).</summary>
    public static AudioClip SinisterLaugh => s_SinisterLaugh ??= BuildLaugh();

    /// <summary>Gold spilling onto the ground: a scatter of bright metallic ticks (~1.1 s).</summary>
    public static AudioClip TreasureLand => s_TreasureLand ??= BuildTreasureLand();

    /// <summary>The Coronado taking an ember hit: a harsh shriek falling from a shout to a growl (~0.4 s).</summary>
    public static AudioClip CoronadoScream => s_CoronadoScream ??= BuildCoronadoScream();

    /// <summary>
    /// The Coronado's breathing: a low, grave, continuous loop (~3.2 s cycle) -
    /// the main localization channel (see JEFE_FINAL.md 6). Meant to be played
    /// on a 3D AudioSource so the player can turn toward it by ear alone.
    /// </summary>
    public static AudioClip CoronadoBreath => s_CoronadoBreath ??= BuildCoronadoBreath();

    /// <summary>Fire-and-forget 3D one-shot at a world position (the source may already be destroyed).</summary>
    public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        var go = new GameObject("SFX " + clip.name);
        go.transform.position = position;
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.spatialBlend = 1f;
        source.volume = volume;
        source.pitch = pitch;
        source.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
    }

    static float Noise(System.Random r) => (float)(r.NextDouble() * 2.0 - 1.0);

    static AudioClip Build(string name, float duration, System.Func<float, System.Random, float> sample)
    {
        int count = Mathf.RoundToInt(k_SampleRate * duration);
        var samples = new float[count];
        var random = new System.Random(name.GetHashCode());
        for (int i = 0; i < count; i++)
            samples[i] = Mathf.Clamp(sample(i / (float)k_SampleRate, random), -1f, 1f);

        var clip = AudioClip.Create(name, count, 1, k_SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    static AudioClip BuildWhoosh()
    {
        const float duration = 1.2f;
        int count = Mathf.RoundToInt(k_SampleRate * duration);
        var samples = new float[count];
        var random = new System.Random(11);
        float filtered = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)k_SampleRate;
            // Whoosh: noise whose brightness and loudness swell fast then fall away.
            float swell = Mathf.Clamp01(t / 0.12f) * Mathf.Exp(-Mathf.Max(0f, t - 0.12f) * 2.6f);
            filtered = Mathf.Lerp(filtered, Noise(random), Mathf.Lerp(0.05f, 0.5f, swell));
            float value = filtered * swell * 1.1f;
            // Crackle pops, dense at first and thinning out.
            if (random.NextDouble() < 0.0025 * Mathf.Exp(-t * 1.8f))
                value += Noise(random) * 0.7f;
            samples[i] = Mathf.Clamp(value, -1f, 1f);
        }

        var clip = AudioClip.Create("FireWhoosh", count, 1, k_SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    static AudioClip BuildBirdSong()
    {
        const float duration = 9f;
        int count = Mathf.RoundToInt(k_SampleRate * duration);
        var samples = new float[count];
        var random = new System.Random(21);

        // Soft breeze underneath.
        float wind = 0f;
        for (int i = 0; i < count; i++)
        {
            wind = Mathf.Lerp(wind, Noise(random), 0.02f);
            float swell = 0.6f + 0.4f * Mathf.Sin(i / (float)k_SampleRate * 0.7f);
            samples[i] = wind * 0.25f * swell;
        }

        // Several birds: each a phrase of chirps (fast downward/upward sweeps) or a trill.
        for (int bird = 0; bird < 4; bird++)
        {
            float baseFreq = 2400f + bird * 650f;
            float t = 0.3f + bird * 0.45f;
            while (t < duration - 0.6f)
            {
                bool trill = random.NextDouble() < 0.35;
                int notes = trill ? 7 + random.Next(4) : 2 + random.Next(3);
                float spacing = trill ? 0.065f : 0.17f;
                for (int n = 0; n < notes; n++)
                {
                    float sweep = (random.NextDouble() < 0.5 ? 1f : -1f) * (500f + (float)random.NextDouble() * 900f);
                    AddChirp(samples, t + n * spacing, 0.075f + (trill ? 0f : 0.05f),
                        baseFreq + (float)random.NextDouble() * 300f, sweep, 0.16f);
                }
                t += notes * spacing + 0.8f + (float)random.NextDouble() * 1.8f;
            }
        }

        for (int i = 0; i < count; i++)
        {
            float fade = Mathf.Clamp01(i / (k_SampleRate * 0.4f)) * Mathf.Clamp01((count - i) / (k_SampleRate * 0.8f));
            samples[i] = Mathf.Clamp(samples[i] * fade, -1f, 1f);
        }

        var clip = AudioClip.Create("BirdSong", count, 1, k_SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    static void AddChirp(float[] samples, float start, float length, float freq, float sweep, float gain)
    {
        int first = Mathf.RoundToInt(start * k_SampleRate);
        int n = Mathf.RoundToInt(length * k_SampleRate);
        float phase = 0f;
        for (int i = 0; i < n && first + i < samples.Length; i++)
        {
            float u = i / (float)n;
            float f = freq + sweep * u + Mathf.Sin(u * 40f) * 90f; // warble
            phase += 2f * Mathf.PI * f / k_SampleRate;
            float env = Mathf.Sin(Mathf.PI * u);
            samples[first + i] += Mathf.Sin(phase) * env * env * gain;
        }
    }

    /// <summary>
    /// The treasure landing beside the fire: coins tapping the packed earth.
    /// Seven overlapping bursts of inharmonic partials, so each one rings like
    /// metal rather than a musical note.
    /// </summary>
    static AudioClip BuildTreasureLand()
    {
        const float duration = 1.1f;
        int count = Mathf.RoundToInt(k_SampleRate * duration);
        var samples = new float[count];
        var random = new System.Random(47);

        for (int coin = 0; coin < 7; coin++)
        {
            float start = 0.02f + (float)random.NextDouble() * 0.35f + coin * 0.03f;
            float pitch = 1500f + (float)random.NextDouble() * 1800f;
            float gain = 0.5f + (float)random.NextDouble() * 0.5f;
            int first = Mathf.RoundToInt(start * k_SampleRate);
            int n = Mathf.RoundToInt((0.18f + (float)random.NextDouble() * 0.12f) * k_SampleRate);

            for (int i = 0; i < n && first + i < count; i++)
            {
                float t = i / (float)k_SampleRate;
                float envelope = Mathf.Exp(-t * 26f);
                float value = Mathf.Sin(2f * Mathf.PI * pitch * t)
                    + 0.55f * Mathf.Sin(2f * Mathf.PI * pitch * 1.71f * t)
                    + 0.35f * Mathf.Sin(2f * Mathf.PI * pitch * 2.43f * t);
                samples[first + i] += value * envelope * gain * 0.28f;
            }
        }

        for (int i = 0; i < count; i++)
            samples[i] = Mathf.Clamp(samples[i], -1f, 1f);

        var clip = AudioClip.Create("TreasureLand", count, 1, k_SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// A harsh, falling shriek for the Coronado taking a hit: pitch drops fast
    /// from a shout down to a growl, with a raspy noise layer under the voiced
    /// tone so it reads as pained rather than musical.
    /// </summary>
    static AudioClip BuildCoronadoScream()
    {
        const float duration = 0.4f;
        int count = Mathf.RoundToInt(k_SampleRate * duration);
        var samples = new float[count];
        var random = new System.Random(59);

        float phase = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)k_SampleRate;
            float u = t / duration;

            float f0 = Mathf.Lerp(1400f, 220f, u * u);
            phase += 2f * Mathf.PI * f0 / k_SampleRate;

            float voiced = Mathf.Sin(phase) + 0.6f * Mathf.Sin(phase * 2f) + 0.35f * Mathf.Sin(phase * 3f);
            float rasp = Noise(random) * 0.5f;
            float env = Mathf.Clamp01(t / 0.01f) * Mathf.Exp(-u * 3.5f);

            samples[i] = (voiced * 0.5f + rasp) * env;
        }

        for (int i = 0; i < count; i++)
            samples[i] = Mathf.Clamp(samples[i] * 1.3f, -1f, 1f);

        var clip = AudioClip.Create("CoronadoScream", count, 1, k_SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// A low, grave, continuous breathing loop for the Coronado: a filtered-noise
    /// rumble under a very low tone, swelling in and falling away twice per cycle
    /// (in - out), with the seams cross-faded so it loops with no audible click.
    /// </summary>
    static AudioClip BuildCoronadoBreath()
    {
        const float duration = 3.2f;
        int count = Mathf.RoundToInt(k_SampleRate * duration);
        var samples = new float[count];
        var random = new System.Random(83);

        float filtered = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)k_SampleRate;
            float cyclePos = t / duration;

            // Two humps per cycle (inhale, exhale), each swelling then fading.
            float breathPhase = cyclePos * Mathf.PI * 2f;
            float envelope = 0.5f + 0.5f * Mathf.Sin(breathPhase - Mathf.PI * 0.5f);
            envelope = Mathf.Pow(envelope, 1.5f);

            float white = Noise(random);
            filtered = Mathf.Lerp(filtered, white, 0.04f);
            float tone = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.3f;

            samples[i] = (filtered * 0.6f + tone) * envelope * 0.8f;
        }

        int fadeSamples = Mathf.Min(2000, count / 8);
        for (int i = 0; i < fadeSamples; i++)
        {
            float t = i / (float)fadeSamples;
            samples[i] = Mathf.Lerp(samples[count - fadeSamples + i], samples[i], t);
        }

        for (int i = 0; i < count; i++)
            samples[i] = Mathf.Clamp(samples[i], -1f, 1f);

        var clip = AudioClip.Create("CoronadoBreath", count, 1, k_SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    static AudioClip BuildLaugh()
    {
        const float duration = 3.6f;
        int count = Mathf.RoundToInt(k_SampleRate * duration);
        var samples = new float[count];
        var random = new System.Random(33);

        // "Ha" bursts: slow at first, then a quickening cackle. Each is a voiced buzz
        // (harmonic stack shaped like an open-mouth vowel) with a falling pitch and a breath of noise.
        float[] starts = { 0.15f, 0.85f, 1.45f, 1.9f, 2.3f, 2.65f };
        float[] lengths = { 0.5f, 0.45f, 0.3f, 0.28f, 0.26f, 0.5f };
        float[] pitches = { 118f, 104f, 100f, 96f, 92f, 84f };

        for (int b = 0; b < starts.Length; b++)
        {
            int first = Mathf.RoundToInt(starts[b] * k_SampleRate);
            int n = Mathf.RoundToInt(lengths[b] * k_SampleRate);
            float phase = 0f;
            for (int i = 0; i < n && first + i < count; i++)
            {
                float u = i / (float)n;
                float f0 = pitches[b] * (1.15f - 0.3f * u) * (1f + 0.02f * Mathf.Sin(u * 30f));
                phase += 2f * Mathf.PI * f0 / k_SampleRate;

                float voiced = 0f;
                for (int h = 1; h <= 14; h++)
                {
                    float freq = f0 * h;
                    // Two vowel-like resonances (~700 Hz and ~1200 Hz).
                    float formant = Mathf.Exp(-Mathf.Pow((freq - 700f) / 350f, 2f)) + 0.6f * Mathf.Exp(-Mathf.Pow((freq - 1200f) / 400f, 2f));
                    voiced += Mathf.Sin(phase * h) * formant / h;
                }

                float attack = Mathf.Clamp01(u / 0.08f);
                float env = attack * Mathf.Exp(-u * 3.2f);
                float breath = Noise(random) * 0.5f * Mathf.Exp(-u * 30f);
                samples[first + i] += (voiced * 0.55f + breath * 0.25f) * env;
            }
        }

        // A touch of slap-back echo for a cavernous feel.
        int delay = Mathf.RoundToInt(0.18f * k_SampleRate);
        for (int i = count - 1; i >= delay; i--)
            samples[i] += samples[i - delay] * 0.35f;

        for (int i = 0; i < count; i++)
            samples[i] = Mathf.Clamp(samples[i] * 1.6f, -1f, 1f);

        var clip = AudioClip.Create("SinisterLaugh", count, 1, k_SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
