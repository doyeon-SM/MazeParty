using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class DevelopmentDiagnosticsPrefabTests
    {
        [Test]
        public void BuildSceneInjection_UsesAuthoredPrefabOnlyForDevelopmentAndNeverStores()
        {
            var installer = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("MazeParty.Editor.FeedbackUiProjectSetup"))
                .First(t => t != null).GetMethod("InstallDiagnosticsForScene", BindingFlags.Public | BindingFlags.Static);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var flags in new[] { (Dev: false, Store: false), (Dev: true, Store: true), (Dev: false, Store: true), (Dev: true, Store: false) })
                {
                    installer.Invoke(null, new object[] { scene, flags.Dev, flags.Store });
                    var views = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<DevelopmentDiagnosticsPanel>(true)).ToArray();
                    Assert.That(views.Length, Is.EqualTo(flags.Dev && !flags.Store ? 1 : 0));
                    if (views.Length == 0) continue;
                    Assert.That(views[0].HasRequiredReferences, Is.True);
                    Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(views[0].gameObject),
                        Is.EqualTo("Assets/MazeParty/Prefabs/Multiplayer/UI/DevelopmentDiagnostics.prefab"));
                    installer.Invoke(null, new object[] { scene, flags.Dev, flags.Store });
                    Assert.That(scene.GetRootGameObjects().Length, Is.EqualTo(1), "Repeated setup must not duplicate the panel.");
                }
                installer.Invoke(null, new object[] { scene, true, true });
                Assert.That(scene.GetRootGameObjects(), Is.Empty, "A store scene must also strip a previously installed panel.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
