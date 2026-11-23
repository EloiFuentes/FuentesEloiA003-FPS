using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > FPS > Configurar sons de les armes
///  1) Posa els sons de tret, de tret en buit i d'equipar a cada WeaponData.
///  2) Afegeix Animation Events "PlaySound" a les animacions de recàrrega dels FBX.
///     Els temps es calculen llegint el moviment dels ossos del carregador i de la corredera,
///     així el so queda sincronitzat amb el moment en què el carregador surt i entra.
/// Els events els rep WeaponAnimationEvents.PlaySound(AudioClip) a l'objecte de l'Animator.
/// </summary>
public static class WeaponSoundSetup
{
    private const string SfxFolder = "Assets/Audio/SFX/Weapons/";
    private const string RifleFbx = "Assets/Models/m4-fps-weapon-animations-pack-v1/source/M4.fbx";
    private const string PistolFbx = "Assets/Models/mark-23-animated-free/source/Mark23.fbx";
    private const float SampleRate = 120f;
    // Els talls de so tenen el cop principal ("clac") just a 0.15 s de l'inici del clip.
    private const float HitOffset = 0.15f;

    [MenuItem("Tools/FPS/Configurar sons de les armes", priority = 41)]
    public static void Setup()
    {
        var log = new StringBuilder("[WeaponSoundSetup]\n");

        AudioClip Load(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxFolder + name + ".wav");
            if (clip == null) log.AppendLine("  FALTA el so: " + name);
            return clip;
        }

        // 1) Sons a les dades de les armes
        foreach (string guid in AssetDatabase.FindAssets("t:WeaponData"))
        {
            var data = AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data == null) continue;
            bool pistol = data.slot == WeaponSlot.Secondary;
            string shot = pistol ? "pistola_disparo" : "AK_disparo";

            Undo.RecordObject(data, "Sons de l'arma");
            data.fireSounds = new[] { Load(shot) };             // sempre el mateix so
            data.firePitchVariation = 0f;                     // sense variació de to
            data.dryFireSound = null;
            data.equipSound = null;
            data.reloadSound = null; // la recàrrega sona amb Animation Events
            EditorUtility.SetDirty(data);
            log.AppendLine($"  {data.name}: sons de {(pistol ? "pistola" : "fusell")}");
        }

        // 2) Animation Events a les recàrregues
        AddReloadEvents(RifleFbx, new[] { "mag1", "mag2" }, "bolt_j",
            Load("AK_cargador_fuera"), Load("AK_cargador_dentro"), Load("AK_cerrojo_atras"), Load("AK_cerrojo_adelante"), log);
        AddReloadEvents(PistolFbx, new[] { "mag" }, "side",
            Load("pistola_cargador_fuera"), Load("pistola_cargador_dentro"), null, Load("pistola_corredera"), log);

        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    private static void AddReloadEvents(string fbxPath, string[] magBones, string boltBone,
        AudioClip magOut, AudioClip magIn, AudioClip boltPull, AudioClip boltRelease, StringBuilder log)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            log.AppendLine("  No trobo " + fbxPath);
            return;
        }

        Dictionary<string, AnimationClip> sourceClips = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
            .OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;

        bool changed = false;
        foreach (ModelImporterClipAnimation clipSettings in clips)
        {
            if (!clipSettings.name.ToLowerInvariant().Contains("reload")) continue;
            if (!sourceClips.TryGetValue(clipSettings.name, out AnimationClip clip)) continue;

            float length = clip.length;
            var events = new List<AnimationEvent>();

            // Carregador: quan surt de la seva posició inicial i quan arriba a la final
            float outTime = float.MaxValue, inTime = -1f;
            foreach (string bone in magBones)
            {
                if (!TryAnalyse(clip, bone, out float leaves, out float arrives)) continue;
                outTime = Mathf.Min(outTime, leaves);
                inTime = Mathf.Max(inTime, arrives);
            }
            if (outTime < float.MaxValue) AddEvent(events, magOut, outTime - HitOffset, length);
            if (inTime > 0f) AddEvent(events, magIn, inTime - HitOffset, length);

            // Forrellat / corredera
            if (TryAnalyse(clip, boltBone, out float boltBack, out float boltHome))
            {
                // Si ja estava enrere a l'inici (arma buida) o el moviment és molt curt,
                // només sona quan torna endavant.
                bool alreadyBack = boltBack < 0.1f || boltHome - boltBack < 0.2f;
                if (!alreadyBack) AddEvent(events, boltPull, boltBack - HitOffset, length);
                AddEvent(events, boltRelease, boltHome - HitOffset, length);
            }

            // Mantenim els events que no siguin de so
            var kept = (clipSettings.events ?? new AnimationEvent[0]).Where(e => e.functionName != "PlaySound");
            clipSettings.events = kept.Concat(events).OrderBy(e => e.time).ToArray();
            changed = true;

            log.AppendLine($"  {System.IO.Path.GetFileName(fbxPath)} / {clipSettings.name} ({length:0.00}s): " +
                string.Join(", ", events.Select(e => $"{e.objectReferenceParameter?.name} @ {e.time * length:0.00}s")));
        }

        if (!changed)
        {
            log.AppendLine("  Cap animació de recàrrega a " + fbxPath);
            return;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    private static void AddEvent(List<AnimationEvent> events, AudioClip sound, float seconds, float length)
    {
        if (sound != null) events.Add(MakeEvent(sound, seconds, length));
    }

    private static AnimationEvent MakeEvent(AudioClip sound, float seconds, float length)
    {
        return new AnimationEvent
        {
            functionName = "PlaySound",
            objectReferenceParameter = sound,
            time = Mathf.Clamp01(seconds / Mathf.Max(0.0001f, length)), // al ModelImporter el temps va normalitzat (0-1)
            messageOptions = SendMessageOptions.DontRequireReceiver
        };
    }

    /// <summary>
    /// Mira la posició local d'un os durant l'animació.
    /// leaves = primer moment en què s'allunya de la posició inicial.
    /// arrives = moment en què arriba (i es queda) a la posició final.
    /// </summary>
    private static bool TryAnalyse(AnimationClip clip, string boneKey, out float leaves, out float arrives)
    {
        leaves = arrives = 0f;
        AnimationCurve[] best = null;
        float bestRange = 0f;

        var bindings = AnimationUtility.GetCurveBindings(clip)
            .Where(b => b.propertyName.StartsWith("m_LocalPosition."))
            .GroupBy(b => b.path)
            .Where(g => g.Key.Split('/').Last().ToLowerInvariant().StartsWith(boneKey));

        foreach (var group in bindings)
        {
            var curves = new AnimationCurve[3];
            foreach (EditorCurveBinding b in group)
            {
                int axis = b.propertyName.EndsWith("x") ? 0 : b.propertyName.EndsWith("y") ? 1 : 2;
                curves[axis] = AnimationUtility.GetEditorCurve(clip, b);
            }
            float range = MaxDistance(curves, clip.length, 0f);
            if (range > bestRange) { bestRange = range; best = curves; }
        }
        if (best == null || bestRange < 1e-4f) return false;

        float threshold = bestRange * 0.08f;
        int steps = Mathf.CeilToInt(clip.length * SampleRate);
        Vector3 start = Sample(best, 0f), end = Sample(best, clip.length);

        leaves = -1f;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / SampleRate;
            if ((Sample(best, t) - start).magnitude > threshold) { leaves = t; break; }
        }
        arrives = -1f;
        for (int i = steps; i >= 0; i--)
        {
            float t = i / SampleRate;
            if ((Sample(best, t) - end).magnitude > threshold) { arrives = Mathf.Min(clip.length, t + 1f / SampleRate); break; }
        }
        return leaves >= 0f && arrives >= 0f;
    }

    private static float MaxDistance(AnimationCurve[] curves, float length, float from)
    {
        Vector3 start = Sample(curves, from);
        float max = 0f;
        int steps = Mathf.CeilToInt(length * SampleRate);
        for (int i = 0; i <= steps; i++)
            max = Mathf.Max(max, (Sample(curves, i / SampleRate) - start).magnitude);
        return max;
    }

    private static Vector3 Sample(AnimationCurve[] c, float t)
    {
        return new Vector3(c[0] != null ? c[0].Evaluate(t) : 0f,
                           c[1] != null ? c[1].Evaluate(t) : 0f,
                           c[2] != null ? c[2].Evaluate(t) : 0f);
    }
}
