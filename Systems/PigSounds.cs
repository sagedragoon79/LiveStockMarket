using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// Pig voices. Vanilla livestock carry no voice of their own (only pets have an
    /// ambient AudioDef; the barnyard bleats come from the ambience layer), so this
    /// is additive. The pattern is Grave Matters' zombie groans: a dedicated child
    /// AudioSource per animal, the mixer group borrowed from a live game source so
    /// the player's sound sliders apply, 3D with a range cap, and a scheduler that
    /// tries several animals before giving up on a turn.
    ///   • Grunts: a random pig every GruntIntervalMin..Max seconds of game time (a
    ///     paused game is silent), one-shot on the pig's voice source.
    ///   • Breathing: a quiet loop on every pig, started at a random offset with a
    ///     slight pitch spread so a herd does not phase; shorter range than grunts.
    ///   • Squeal: on slaughter, from a temporary source at the pig's position — the
    ///     pig object is destroyed in the same call, so its own source would be cut off.
    /// Clips come from the pig bundle (mono, Vorbis), loaded by PigAssets.
    /// </summary>
    internal static class PigSounds
    {
        private const string VoiceName  = "LSM_PigVoice";
        private const string BreathName = "LSM_PigBreath";
        private const string SquealName = "LSM_PigSqueal";
        private const string ClickName  = "LSM_PigClick";

        private static AudioSource _click;
        private static float _nextClick;
        private static bool _loggedFirstClick, _loggedFirstBarnClick;

        private static readonly List<PigLook> _live = new List<PigLook>();
        private static AudioMixerGroup _bus;
        private static float _nextBusLookup;
        private static float _nextGrunt;
        private static bool _loggedFirstGrunt, _loggedNoBus;

        private static string Tag => LiveStockMarketMod.LogTag;

        internal static bool  Enabled         => LiveStockMarketMod.cfgPigSounds?.Value ?? true;
        internal static float GruntVolume     => Mathf.Clamp01(LiveStockMarketMod.cfgPigGruntVolume?.Value ?? 0.6f);
        internal static float BreathingVolume => Mathf.Clamp01(LiveStockMarketMod.cfgPigBreathingVolume?.Value ?? 0.25f);
        internal static float IntervalMin     => Mathf.Max(1f, LiveStockMarketMod.cfgPigGruntIntervalMin?.Value ?? 6f);
        internal static float IntervalMax     => Mathf.Max(IntervalMin + 1f, LiveStockMarketMod.cfgPigGruntIntervalMax?.Value ?? 20f);
        internal static float Range           => Mathf.Clamp(LiveStockMarketMod.cfgPigSoundRange?.Value ?? 40f, 5f, 300f);
        internal static bool  ClickEnabled    => LiveStockMarketMod.cfgPigClickSound?.Value ?? true;
        /// <summary>0 = the dedicated click grunt (PigClick); 1..N = that ambient grunt (PigGrunt01 is 1); anything else = a random ambient grunt per click.</summary>
        internal static int   ClickGrunt      => LiveStockMarketMod.cfgPigClickGrunt?.Value ?? 0;

        /// <summary>Mixer objects and pig objects do not survive a scene change.</summary>
        public static void OnSceneLoaded()
        {
            _bus = null; _nextBusLookup = 0f; _nextGrunt = 0f; _loggedNoBus = false;
            _click = null; _nextClick = 0f;
            _live.Clear();
        }

        // ── click (selection) sound ──────────────────────────────────────────
        /// <summary>
        /// True when the clicked object is a pig and the mod answers for it, so the caller
        /// skips vanilla's selection effect (the goat's bell and bleat). Flat, not positional:
        /// a click should answer at any zoom, as vanilla's does.
        /// </summary>
        public static bool TryPlayClick(GameObject go)
        {
            try
            {
                if (go == null || !Enabled || !ClickEnabled) return false;
                // a pig, or a goat barn while it is a Pig Barn (its vanilla click is the goat barn's)
                var look = go.GetComponent<PigLook>();
                bool isBarn = false;
                if (look == null || !look.IsPig)
                {
                    var barn = go.GetComponent<GoatBarn>() ?? go.GetComponentInParent<GoatBarn>();
                    if (barn == null || !GoatBarnModeStore.IsPigs(barn)) return false;
                    isBarn = true;
                }
                var clip = ClickClip();
                if (clip == null) return false;               // no clips loaded: leave vanilla alone
                if (Time.unscaledTime >= _nextClick)
                {
                    _nextClick = Time.unscaledTime + 0.6f;    // rapid clicks do not stack
                    PlayFlat(clip);
                    if (isBarn ? !_loggedFirstBarnClick : !_loggedFirstClick)
                    {
                        if (isBarn) _loggedFirstBarnClick = true; else _loggedFirstClick = true;
                        LiveStockMarketMod.Log.Msg($"{Tag} PigSounds: first {(isBarn ? "Pig Barn" : "pig")} click answered with '{clip.name}' ({clip.length:F2} s; setting {ClickGrunt}); the goat {(isBarn ? "barn's own selection sound is" : "bell and bleat are")} skipped for it.");
                    }
                }
                return true;                                   // a pig either way: no bell, no bleat
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} PigSounds.TryPlayClick: {ex.Message}");
                return false;
            }
        }

        /// <summary>Plays the chosen click grunt once, so moving the setting auditions it.</summary>
        public static void PreviewClick()
        {
            try
            {
                if (!LiveStockMarketMod.InMap) return;
                var clip = ClickClip();
                if (clip != null) PlayFlat(clip);
            }
            catch (Exception ex) { LiveStockMarketMod.Log.Warning($"{Tag} PigSounds.PreviewClick: {ex.Message}"); }
        }

        private static AudioClip ClickClip()
        {
            int n = ClickGrunt;
            if (n == 0 && PigAssets.Click != null) return PigAssets.Click;   // the hand-made click grunt
            var clips = PigAssets.Grunts;
            if (clips == null || clips.Count == 0) return PigAssets.Click;
            if (n == 0) return clips[Mathf.Min(8, clips.Count - 1)];         // bundle without PigClick: grunt 9 stands in
            if (n >= 1 && n <= clips.Count) return clips[n - 1];             // sorted by name: PigGrunt01 is index 0
            return clips[UnityEngine.Random.Range(0, clips.Count)];
        }

        private static void PlayFlat(AudioClip clip)
        {
            if (_click == null)
            {
                var go = new GameObject(ClickName);
                _click = go.AddComponent<AudioSource>();
                _click.playOnAwake  = false;
                _click.loop         = false;
                _click.spatialBlend = 0f;
            }
            if (_click.outputAudioMixerGroup == null) _click.outputAudioMixerGroup = Bus();
            _click.PlayOneShot(clip, GruntVolume);
        }

        // ── per pig ──────────────────────────────────────────────────────────
        public static void Attach(PigLook look, LivestockAnimal animal)
        {
            try
            {
                if (look == null || animal == null) return;
                if (look.Voice == null) look.Voice = MakeSource(animal.transform, VoiceName, loop: false);
                if (look.Breath == null && PigAssets.Breathing != null)
                {
                    var b = MakeSource(animal.transform, BreathName, loop: true);
                    b.clip  = PigAssets.Breathing;
                    b.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
                    b.time  = UnityEngine.Random.Range(0f, Mathf.Max(0f, PigAssets.Breathing.length - 0.1f));
                    look.Breath = b;
                }
                if (!_live.Contains(look)) _live.Add(look);
                ApplyTo(look);
            }
            catch (Exception ex) { LiveStockMarketMod.Log.Warning($"{Tag} PigSounds.Attach: {ex.Message}"); }
        }

        public static void Detach(PigLook look)
        {
            try
            {
                if (look == null) return;
                _live.Remove(look);
                if (look.Voice  != null) UnityEngine.Object.Destroy(look.Voice.gameObject);
                if (look.Breath != null) UnityEngine.Object.Destroy(look.Breath.gameObject);
                look.Voice = null; look.Breath = null;
            }
            catch (Exception ex) { LiveStockMarketMod.Log.Warning($"{Tag} PigSounds.Detach: {ex.Message}"); }
        }

        private static AudioSource MakeSource(Transform parent, string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.4f, 0f);   // about snout height
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake  = false;
            src.loop         = loop;
            src.spatialBlend = 1f;
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.dopplerLevel = 0f;
            src.spread       = 0f;
            src.outputAudioMixerGroup = Bus();
            return src;
        }

        /// <summary>Prefs onto a live pig: volumes, ranges, routing, the breathing loop's on/off.</summary>
        private static void ApplyTo(PigLook look)
        {
            float range = Range;
            if (look.Voice != null)
            {
                look.Voice.minDistance = Mathf.Min(4f, range * 0.25f);
                look.Voice.maxDistance = range;
                if (look.Voice.outputAudioMixerGroup == null) look.Voice.outputAudioMixerGroup = Bus();
            }
            if (look.Breath != null)
            {
                look.Breath.volume      = BreathingVolume;
                look.Breath.minDistance = Mathf.Min(2f, range * 0.1f);
                look.Breath.maxDistance = range * 0.4f;
                if (look.Breath.outputAudioMixerGroup == null) look.Breath.outputAudioMixerGroup = Bus();
                bool shouldPlay = Enabled && BreathingVolume > 0f && look.Breath.clip != null;
                if (shouldPlay && !look.Breath.isPlaying) look.Breath.Play();
                else if (!shouldPlay && look.Breath.isPlaying) look.Breath.Stop();
            }
        }

        public static void ApplyPrefs()
        {
            try { Prune(); foreach (var l in _live) ApplyTo(l); }
            catch (Exception ex) { LiveStockMarketMod.Log.Warning($"{Tag} PigSounds.ApplyPrefs: {ex.Message}"); }
        }

        private static void Prune() => _live.RemoveAll(l => l == null || !l.IsPig);

        // ── grunt scheduler (every frame from OnUpdate; cheap until the timer comes round) ──
        public static void Tick()
        {
            try
            {
                if (!Enabled || !LiveStockMarketMod.InMap) return;
                if (Time.time < _nextGrunt) return;
                _nextGrunt = Time.time + UnityEngine.Random.Range(IntervalMin, IntervalMax);
                Prune();
                if (_live.Count == 0) return;
                var clips = PigAssets.Grunts;
                if (clips == null || clips.Count == 0) return;

                int tries = Mathf.Clamp(_live.Count, 1, 5);
                for (int i = 0; i < tries; i++)
                {
                    var look = _live[UnityEngine.Random.Range(0, _live.Count)];
                    var src = look != null ? look.Voice : null;
                    if (src == null || !src.isActiveAndEnabled || src.isPlaying) continue;
                    ApplyTo(look);
                    var clip = clips[UnityEngine.Random.Range(0, clips.Count)];
                    src.PlayOneShot(clip, GruntVolume);
                    if (!_loggedFirstGrunt)
                    {
                        _loggedFirstGrunt = true;
                        LiveStockMarketMod.Log.Msg($"{Tag} PigSounds: first grunt '{clip.name}' ({clip.length:F2} s) from one of {_live.Count} pig(s), " +
                            $"bus {(src.outputAudioMixerGroup != null ? "'" + src.outputAudioMixerGroup.name + "'" : "NONE")}, range {src.maxDistance:F0} m, volume {GruntVolume:F2}.");
                    }
                    return;
                }
                _nextGrunt = Time.time + 1.5f;   // every candidate was busy or culled: come back sooner
            }
            catch (Exception ex) { LiveStockMarketMod.Log.Warning($"{Tag} PigSounds.Tick: {ex.Message}"); }
        }

        // ── slaughter ────────────────────────────────────────────────────────
        public static void PlaySqueal(Vector3 position)
        {
            try
            {
                if (!Enabled) return;
                var clips = PigAssets.Squeals;
                if (clips == null || clips.Count == 0) return;
                var clip = clips[UnityEngine.Random.Range(0, clips.Count)];
                var go = new GameObject(SquealName);
                go.transform.position = position + Vector3.up * 0.4f;
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake  = false;
                src.loop         = false;
                src.spatialBlend = 1f;
                src.rolloffMode  = AudioRolloffMode.Linear;
                src.dopplerLevel = 0f;
                src.minDistance  = Mathf.Min(4f, Range * 0.25f);
                src.maxDistance  = Range * 1.5f;
                src.outputAudioMixerGroup = Bus();
                src.clip   = clip;
                src.volume = GruntVolume;
                src.Play();
                UnityEngine.Object.Destroy(go, clip.length + 0.2f);
            }
            catch (Exception ex) { LiveStockMarketMod.Log.Warning($"{Tag} PigSounds.PlaySqueal: {ex.Message}"); }
        }

        // ── mixer ────────────────────────────────────────────────────────────
        /// <summary>
        /// A fresh AudioSource has no mixer group and bypasses the game's volume sliders.
        /// Borrow the group a live game source uses. Retried every few seconds until one
        /// is found, because pigs can be attached before the ambience sources exist.
        /// </summary>
        private static AudioMixerGroup Bus()
        {
            if (_bus != null) return _bus;
            if (Time.realtimeSinceStartup < _nextBusLookup) return null;
            _nextBusLookup = Time.realtimeSinceStartup + 5f;
            try
            {
                foreach (var src in UnityEngine.Object.FindObjectsOfType<AudioSource>())
                {
                    if (src == null || src.outputAudioMixerGroup == null) continue;
                    var n = src.gameObject.name;
                    if (n == VoiceName || n == BreathName || n == SquealName || n == ClickName) continue;
                    _bus = src.outputAudioMixerGroup;
                    LiveStockMarketMod.Log.Msg($"{Tag} PigSounds: routing through mixer group '{_bus.name}' (borrowed from '{n}').");
                    break;
                }
                if (_bus == null && !_loggedNoBus)
                {
                    _loggedNoBus = true;
                    LiveStockMarketMod.Log.Msg($"{Tag} PigSounds: no mixer group to borrow yet — pig sounds stay unrouted until one appears.");
                }
            }
            catch (Exception ex) { LiveStockMarketMod.Log.Warning($"{Tag} PigSounds: mixer lookup failed: {ex.Message}"); }
            return _bus;
        }
    }
}
