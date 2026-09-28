using System;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>
    /// One-time, exact-target migration of the two remaining legacy minigame
    /// HUD assets.
    /// Normal scene setup never invokes this: rerunning setup must not restyle
    /// a designer-authored prefab.
    /// </summary>
    public static class CompactMinigameHudsAProjectSetup
    {
        private const string Folder = "Assets/MazeParty/Prefabs/Minigames/";
        private static readonly HudMigration[] Migrations =
        {
            new HudMigration("RedLightGreenLightHud", new[]
            {
                "Phase", "Instructions", "Player1Row", "Player2Row",
                "Player3Row", "Player4Row"
            }, new[] { "Signal" }),
            new HudMigration("StableFootingHud", new[]
            {
                "Phase", "Timer", "Round", "StatusPanel", "PausePanel",
                "ControlsPanel"
            }, new[] { "HudPanel", "Instructions" })
        };

        [MenuItem("MazeParty/UI/Migrate Two Legacy Minigame HUDs")]
        public static void Migrate()
        {
            // Validate every exact prefab before changing any of them.
            foreach (var migration in Migrations)
            {
                WithPrefabContents(migration, root => Validate(root, migration));
            }

            var changed = 0;
            foreach (var migration in Migrations)
            {
                WithPrefabContents(migration, root =>
                {
                    if (!MigrateContents(root, migration))
                    {
                        return;
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, migration.Path);
                    changed++;
                });
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Migrated " + changed +
                      " of two exact minigame HUD prefabs; " +
                      "existing non-template layout was preserved.");
        }

        private static void WithPrefabContents(
            HudMigration migration,
            Action<GameObject> action)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(migration.Path) == null)
            {
                throw new InvalidOperationException(
                    "HUD prefab is missing: " + migration.Path);
            }

            var root = PrefabUtility.LoadPrefabContents(migration.Path);
            try
            {
                if (root == null || root.name != migration.Name ||
                    root.GetComponent<Canvas>() == null)
                {
                    throw new InvalidOperationException(
                        "HUD root contract changed: " + migration.Path);
                }
                action(root);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void Validate(GameObject root, HudMigration migration)
        {
            foreach (var name in migration.RequiredNames)
            {
                RequireNamed(root.transform, name);
            }
            foreach (var name in migration.ObsoleteNames)
            {
                FindNamed(root.transform, name);
            }
            var timers = root.GetComponentsInChildren<MinigameTimerDial>(true);
            if (timers.Length > 1)
            {
                throw new InvalidOperationException(
                    "Multiple nested timer dials in " + migration.Path);
            }
        }

        private static bool MigrateContents(
            GameObject root,
            HudMigration migration)
        {
            Validate(root, migration);
            var changed = false;
            foreach (var name in migration.ObsoleteNames)
            {
                var obsolete = FindNamed(root.transform, name);
                if (obsolete == null)
                {
                    continue;
                }
                UnityEngine.Object.DestroyImmediate(obsolete.gameObject);
                changed = true;
            }

            var timers = root.GetComponentsInChildren<MinigameTimerDial>(true);
            if (timers.Length == 1)
            {
                UnityEngine.Object.DestroyImmediate(timers[0].gameObject);
                changed = true;
            }

            changed |= CompactTemplateLayout(root, migration.Name);
            if (changed)
            {
                Rebind(root, migration.Name);
            }
            return changed;
        }

        private static bool CompactTemplateLayout(GameObject root, string name)
        {
            var changed = false;
            if (name == "RedLightGreenLightHud")
            {
                changed |= ResizeIfTemplate(root, "HudPanel",
                    new Vector2(1120f, 335f), new Vector2(1120f, 125f));
                changed |= MoveIfTemplate(root, "Signal",
                    new Vector2(0f, -52f), new Vector2(0f, -28f));
            }
            else if (name == "StableFootingHud")
            {
                changed |= ResizeIfTemplate(root, "HudPanel",
                    new Vector2(500f, 190f), new Vector2(500f, 82f));
                changed |= MoveIfTemplate(root, "Instructions",
                    new Vector2(0f, -118f), new Vector2(0f, -22f));
            }
            return changed;
        }

        private static bool ResizeIfTemplate(
            GameObject root, string objectName, Vector2 oldSize, Vector2 newSize)
        {
            var rect = RequireNamed(root.transform, objectName) as RectTransform;
            if (rect == null || Vector2.Distance(rect.sizeDelta, oldSize) > 0.01f)
            {
                return false;
            }
            rect.sizeDelta = newSize;
            return true;
        }

        private static bool MoveIfTemplate(
            GameObject root, string objectName, Vector2 oldPos, Vector2 newPos)
        {
            var rect = RequireNamed(root.transform, objectName) as RectTransform;
            if (rect == null ||
                Vector2.Distance(rect.anchoredPosition, oldPos) > 0.01f)
            {
                return false;
            }
            rect.anchoredPosition = newPos;
            return true;
        }

        private static void Rebind(GameObject root, string name)
        {
            var canvas = root.GetComponent<Canvas>();
            switch (name)
            {
                case "RedLightGreenLightHud":
                    var light = root.GetComponent<RedLightGreenLightHudBindings>();
                    light.Configure(canvas, RequireText(root, "Signal"),
                        light.NeutralSignalColor, light.GreenSignalColor,
                        light.TurnWarningSignalColor, light.RedSignalColor);
                    break;
                case "StableFootingHud":
                    root.GetComponent<StableFootingHudBindings>().Configure(
                        canvas, RequireText(root, "Instructions"));
                    break;
                default:
                    throw new InvalidOperationException("Unexpected HUD: " + name);
            }
        }

        private static Text RequireText(GameObject root, string name)
        {
            var text = RequireNamed(root.transform, name).GetComponent<Text>();
            if (text == null)
            {
                throw new InvalidOperationException(name + " is not a UI Text.");
            }
            return text;
        }

        private static Transform RequireNamed(Transform root, string name)
        {
            var found = FindNamed(root, name);
            if (found == null)
            {
                throw new InvalidOperationException(
                    "Required HUD element is missing: " + root.name + "/" + name);
            }
            return found;
        }

        private static Transform FindNamed(Transform root, string name)
        {
            Transform found = null;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name != name)
                {
                    continue;
                }
                if (found != null)
                {
                    throw new InvalidOperationException(
                        "Ambiguous HUD element: " + root.name + "/" + name);
                }
                found = child;
            }
            return found;
        }

        private sealed class HudMigration
        {
            public HudMigration(string name, string[] obsolete, string[] required)
            {
                Name = name;
                ObsoleteNames = obsolete;
                RequiredNames = required;
            }

            public string Name { get; }
            public string Path => Folder + Name.Substring(0, Name.Length - 3) + "/UI/" + Name + ".prefab";
            public string[] ObsoleteNames { get; }
            public string[] RequiredNames { get; }
        }
    }
}
