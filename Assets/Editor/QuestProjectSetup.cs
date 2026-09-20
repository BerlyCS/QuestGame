using System;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

/// <summary>
/// Batch entry point: configures this project for Meta Quest (Android) using
/// direct editor APIs. Run with:
///   Unity.exe -batchmode -nographics -quit -projectPath &lt;proj&gt; -executeMethod QuestProjectSetup.Configure
/// </summary>
public static class QuestProjectSetup
{
    const string k_UrpFolder = "Assets/Settings";
    const string k_XrFolder = "Assets/XR";

    public static void Configure()
    {
        var log = new StringBuilder();
        Run("Player", ConfigurePlayer, log);
        Run("URP", ConfigureUrp, log);
        Run("XR", ConfigureXr, log);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[QuestProjectSetup]\n" + log);
    }

    static void Run(string name, Action action, StringBuilder log)
    {
        try
        {
            action();
            log.AppendLine("OK   " + name);
        }
        catch (Exception e)
        {
            log.AppendLine("FAIL " + name + " :: " + e.GetType().Name + ": " + e.Message);
        }
    }

    static void ConfigurePlayer()
    {
        PlayerSettings.companyName = "DefaultCompany";
        PlayerSettings.productName = "QuestGame";
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.DefaultCompany.QuestGame");

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)32;
        PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)34;
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

        SetActiveInputHandler(1); // Input System (new)
    }

    static void SetActiveInputHandler(int value)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (assets == null || assets.Length == 0)
            throw new Exception("ProjectSettings.asset not found");
        var so = new SerializedObject(assets[0]);
        var prop = so.FindProperty("activeInputHandler");
        if (prop == null)
            throw new Exception("activeInputHandler property not found");
        prop.intValue = value;
        so.ApplyModifiedProperties();
    }

    static void ConfigureUrp()
    {
        if (!AssetDatabase.IsValidFolder(k_UrpFolder))
            AssetDatabase.CreateFolder("Assets", "Settings");

        var rendererPath = k_UrpFolder + "/URP-Renderer.asset";
        var pipelinePath = k_UrpFolder + "/URP-Asset.asset";

        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, rendererPath);
        }

        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, pipelinePath);
        }

        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
    }

    static void ConfigureXr()
    {
        ConfigureXrForGroup(BuildTargetGroup.Android);
        ConfigureXrForGroup(BuildTargetGroup.Standalone);
    }

    /// <summary>
    /// Batch entry point for the PC (Standalone) OpenXR setup only. XR Plug-in
    /// Management always initializes the Standalone settings when running in the
    /// Editor, regardless of the active build target, so this is what lets the
    /// Meta XR Simulator open during Play mode while the project still ships to
    /// Android. Run with:
    ///   Unity.exe -batchmode -nographics -quit -projectPath &lt;proj&gt; -executeMethod QuestProjectSetup.ConfigureSimulatorXr
    /// </summary>
    public static void ConfigureSimulatorXr()
    {
        ConfigureXrForGroup(BuildTargetGroup.Standalone);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[QuestProjectSetup] Configured Standalone (Desktop) OpenXR for the Meta XR Simulator.");
    }

    static void ConfigureXrForGroup(BuildTargetGroup group)
    {
        var perBt = GetOrCreatePerBuildTarget();

        if (!perBt.HasSettingsForBuildTarget(group))
            perBt.CreateDefaultSettingsForBuildTarget(group);
        if (!perBt.HasManagerSettingsForBuildTarget(group))
            perBt.CreateDefaultManagerSettingsForBuildTarget(group);

        var manager = perBt.ManagerSettingsForBuildTarget(group);
        if (!XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group))
            throw new Exception("Failed to assign OpenXRLoader");

        FeatureHelpers.RefreshFeatures(group);
        var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
        if (oxr == null)
            throw new Exception($"OpenXRSettings ({group}) is null");

        var metaQuest = oxr.GetFeature<MetaQuestFeature>();
        if (metaQuest != null)
            metaQuest.enabled = true;

        var set = OpenXRFeatureSetManager.GetFeatureSetWithId(group, "com.meta.openxr.featureset.metaxr");
        if (set != null)
        {
            set.isEnabled = true;
            OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(group);
        }

        // Controller/hand input profiles. The Meta XR Simulator presents itself as
        // an Oculus Touch controller, so these must be active on Standalone too,
        // not just Android, or the rig has no input in Play mode.
        EnableFeature(group, "com.unity.openxr.feature.input.oculustouch");
        EnableFeature(group, "com.unity.openxr.feature.input.metaquestplus");
        EnableFeature(group, "com.meta.openxr.feature.input.oculustouch.proximity");
        EnableFeature(group, "com.meta.openxr.feature.metaxr");

        FeatureHelpers.RefreshFeatures(group);
        EditorUtility.SetDirty(perBt);
    }

    static void EnableFeature(BuildTargetGroup group, string featureId)
    {
        var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, featureId);
        if (feature != null)
            feature.enabled = true;
    }

    static XRGeneralSettingsPerBuildTarget GetOrCreatePerBuildTarget()
    {
        XRGeneralSettingsPerBuildTarget perBt = null;
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out perBt) || perBt == null)
        {
            perBt = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            if (!AssetDatabase.IsValidFolder(k_XrFolder))
                AssetDatabase.CreateFolder("Assets", "XR");
            AssetDatabase.CreateAsset(perBt, k_XrFolder + "/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perBt, true);
        }
        return perBt;
    }
}
