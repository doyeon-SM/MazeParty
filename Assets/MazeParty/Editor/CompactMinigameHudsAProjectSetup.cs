using System;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>
    /// One-time, exact-target migration of the six older minigame HUD assets.
    /// Normal scene setup never invokes this: rerunning setup must not restyle
    /// a designer-authored prefab.
    /// </summary>
    public static class CompactMinigameHudsAProjectSetup
    {
        private const string Folder = "Assets/MazeParty/UI/Prefabs/";
        private static readonly HudMigration[] Migrations =
        {
            new HudMigration("MinefieldHud", new[]
            {
                "Minefield Phase", "Minefield Instructions"
            }, new[]
            {
                "Minefield Score 0", "Minefield Score 1",
                "Minefield Score 2", "Minefield Score 3"
            }),
            new HudMigration("WrongWayHud", new[]
            {
                "Phase", "Instruction"
            }, new[]
            {
                "Local Prompt", "Player 1 Progress", "Player 2 Progress",
                "Player 3 Progress", "Player 4 Progress"
            }),
            new HudMigration("RedLightGreenLightHud", new[]
            {
                "Phase", "Instructions", "Player1Row", "Player2Row",
                "Player3Row", "Player4Row"
            }, new[] { "Signal" }),
            new HudMigration("StableFootingHud", new[]
            {
                "Phase", "Timer", "Round", "StatusPanel", "PausePanel",
                "ControlsPanel"
            }, new[] { "HudPanel", "Instructions", "ResultPanel", "ResultMessage" }),
            new HudMigration("BalloonBlowHud", new[]
            {
                "Phase", "Timer", "Round", "Pause Panel",
                "Controls Panel", "Player 2 Card", "Player 3 Card",
                "Player 4 Card"
            }, new[]
            {
                "Header Panel", "Instructions", "Player 1 Card",
                "Player 1 Row", "Result Panel",
                "Result Message"
            }),
            new HudMigration("GiftGrabHud", new[]
            {
                "Phase", "Timer", "Round", "Instructions",
                "Loose Gift Count", "Pause Panel", "Controls Panel"
            }, new[]
            {
                "Header Panel", "Local Status", "Player 1 Row",
                "Player 2 Row", "Player 3 Row", "Player 4 Row",
                "Result Panel", "Result Message"
            })
        };

        [MenuItem("MazeParty/UI/Migrate Six Legacy Minigame HUDs")]
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
                      " of six exact minigame HUD prefabs; " +
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
            if (migration.Name == "BalloonBlowHud")
            {
                RequireNamed(
                    RequireNamed(root.transform, "Player 1 Card"),
                    "Progress Fill");
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

            if (migration.Name == "StableFootingHud" ||
                migration.Name == "BalloonBlowHud" ||
                migration.Name == "GiftGrabHud")
            {
                var panelName = migration.Name == "StableFootingHud"
                    ? "ResultPanel"
                    : "Result Panel";
                var panel = RequireNamed(root.transform, panelName).gameObject;
                var canvas = panel.GetComponent<Canvas>();
                if (canvas == null)
                {
                    canvas = panel.AddComponent<Canvas>();
                    canvas.sortingOrder = 100;
                    changed = true;
                }
                // In Unity 6, setting Canvas.overrideSorting on prefab
                // contents can be reset while the nested canvas is saved.
                // Write the serialized flag explicitly and verify it before
                // saving this exact prefab.
                var serializedCanvas = new SerializedObject(canvas);
                var overrideSorting = serializedCanvas.FindProperty(
                    "m_OverrideSorting");
                if (overrideSorting == null)
                {
                    throw new InvalidOperationException(
                        "Canvas override-sorting property is unavailable: " +
                        migration.Path);
                }
                if (!overrideSorting.boolValue)
                {
                    overrideSorting.boolValue = true;
                    serializedCanvas.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(canvas);
                    changed = true;
                }
                if (panel.activeSelf)
                {
                    panel.SetActive(false);
                    changed = true;
                }
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
            if (name == "MinefieldHud")
            {
                changed |= ResizeIfTemplate(root, "Minefield HUD Panel",
                    new Vector2(950f, 265f), new Vector2(950f, 100f));
                for (var slot = 0; slot < 4; slot++)
                {
                    var rowName = "Minefield Score " + slot;
                    changed |= MoveIfTemplate(root, rowName,
                        new Vector2(-330f + slot * 220f, -133f),
                        new Vector2(-330f + slot * 220f, -24f));
                    changed |= ResizeIfTemplate(root, rowName,
                        new Vector2(205f, 105f), new Vector2(205f, 50f));
                }
            }
            else if (name == "WrongWayHud")
            {
                changed |= ResizeIfTemplate(root, "WrongWay HUD Panel",
                    new Vector2(1120f, 330f), new Vector2(1120f, 220f));
                changed |= MoveIfTemplate(root, "Local Prompt",
                    new Vector2(0f, -115f), new Vector2(0f, -20f));
                for (var slot = 0; slot < 4; slot++)
                {
                    changed |= MoveIfTemplate(root,
                        "Player " + (slot + 1) + " Progress",
                        new Vector2(0f, -194f - slot * 29f),
                        new Vector2(0f, -100f - slot * 29f));
                }
            }
            else if (name == "RedLightGreenLightHud")
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
            else if (name == "BalloonBlowHud")
            {
                changed |= ResizeIfTemplate(root, "Header Panel",
                    new Vector2(760f, 164f), new Vector2(560f, 86f));
                changed |= MoveIfTemplate(root, "Instructions",
                    new Vector2(0f, -103f), new Vector2(0f, -22f));
                changed |= ResizeIfTemplate(root, "Instructions",
                    new Vector2(710f, 42f), new Vector2(520f, 42f));
                changed |= MoveIfTemplate(root, "Player 1 Card",
                    new Vector2(-570f, 22f), new Vector2(0f, 22f));
            }
            else if (name == "GiftGrabHud")
            {
                changed |= ResizeIfTemplate(root, "Header Panel",
                    new Vector2(900f, 214f), new Vector2(620f, 90f));
                changed |= MoveIfTemplate(root, "Local Status",
                    new Vector2(0f, -150f), new Vector2(0f, -22f));
                changed |= ResizeIfTemplate(root, "Local Status",
                    new Vector2(850f, 44f), new Vector2(580f, 44f));
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
                case "MinefieldHud":
                    var mineRows = new Text[4];
                    for (var slot = 0; slot < mineRows.Length; slot++)
                    {
                        mineRows[slot] = RequireText(root, "Minefield Score " + slot);
                    }
                    root.GetComponent<MinefieldHudBindings>().Configure(canvas, mineRows);
                    break;
                case "WrongWayHud":
                    var wayRows = new Text[4];
                    for (var slot = 0; slot < wayRows.Length; slot++)
                    {
                        wayRows[slot] = RequireText(root,
                            "Player " + (slot + 1) + " Progress");
                    }
                    root.GetComponent<WrongWayHudBindings>().Configure(
                        canvas, RequireText(root, "Local Prompt"), wayRows);
                    break;
                case "RedLightGreenLightHud":
                    var light = root.GetComponent<RedLightGreenLightHudBindings>();
                    light.Configure(canvas, RequireText(root, "Signal"),
                        light.NeutralSignalColor, light.GreenSignalColor,
                        light.TurnWarningSignalColor, light.RedSignalColor);
                    break;
                case "StableFootingHud":
                    root.GetComponent<StableFootingHudBindings>().Configure(
                        canvas, RequireText(root, "Instructions"),
                        RequireNamed(root.transform, "ResultPanel").gameObject);
                    break;
                case "BalloonBlowHud":
                    root.GetComponent<BalloonBlowHudBindings>().Configure(
                        canvas, RequireText(root, "Instructions"),
                        RequireText(root, "Player 1 Row"),
                        RequireNamed(
                                RequireNamed(root.transform, "Player 1 Card"),
                                "Progress Fill")
                            .GetComponent<Image>(),
                        RequireText(root, "Result Message"),
                        RequireNamed(root.transform, "Result Panel").gameObject);
                    break;
                case "GiftGrabHud":
                    var gift = root.GetComponent<GiftGrabHudBindings>();
                    var giftRows = new Text[4];
                    for (var slot = 0; slot < giftRows.Length; slot++)
                    {
                        giftRows[slot] = RequireText(root,
                            "Player " + (slot + 1) + " Row");
                    }
                    gift.Configure(canvas, RequireText(root, "Local Status"),
                        giftRows, RequireText(root, "Result Message"),
                        RequireNamed(root.transform, "Result Panel").gameObject,
                        gift.LocalPlayerRowColor);
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
            public string Path => Folder + Name + ".prefab";
            public string[] ObsoleteNames { get; }
            public string[] RequiredNames { get; }
        }
    }
}
