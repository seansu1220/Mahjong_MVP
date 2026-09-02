using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mahjong.EditorTools
{
    // ============================================================
    // WebGL 建置
    //
    // 這支腳本存在的理由：建置設定不能只存在某個人的 Unity 介面裡。
    // 有幾項設定漏掉就會做出一個「跑得起來但畫面全壞」的版本，
    // 而且在編輯器裡完全看不出來——只有建置後開瀏覽器才會發現。
    // 所以每次建置前都由程式重新套一次，不依賴任何人記得去勾。
    //
    // 用法：
    //   編輯器內  選單 Mahjong ▸ Build WebGL
    //   命令列    Unity.exe -quit -batchmode -nographics ^
    //               -projectPath <專案> ^
    //               -executeMethod Mahjong.EditorTools.WebGLBuild.BuildFromCommandLine ^
    //               -logFile <log>
    //
    // 命令列建置時 Unity 編輯器必須先關掉，否則專案資料夾是鎖住的。
    // ============================================================

    public static class WebGLBuild
    {
        const string OutputFolder = "Build/WebGL";

        /// <summary>
        /// 執行期用 Shader.Find 建材質的著色器。
        ///
        /// 專案開了 Strip Engine Code，而牌的材質全部是執行期才建的，
        /// 建置時掃不到任何引用，這些著色器會被整個剝掉——
        /// 結果就是所有的牌變成紫色。列在這裡強制打包進去。
        /// </summary>
        static readonly string[] RuntimeShaders =
        {
            "Standard",
            "Sprites/Default",
            "UI/Default",
            "Unlit/Texture"
        };

        // ------------------------------------------------------------

        [MenuItem("Mahjong/Build WebGL")]
        public static void BuildFromMenu()
        {
            var report = Build();
            if (report.summary.result == BuildResult.Succeeded)
                EditorUtility.RevealInFinder(Path.GetFullPath(OutputFolder));
        }

        /// <summary>命令列進入點。失敗時以非 0 離開，CI 或指令碼才判斷得出來。</summary>
        public static void BuildFromCommandLine()
        {
            var report = Build();
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        // ------------------------------------------------------------

        static BuildReport Build()
        {
            EnsureRuntimeShadersIncluded();
            ApplyPlayerSettings();

            var scenes = EditorBuildSettings.scenes
                                            .Where(s => s.enabled)
                                            .Select(s => s.path)
                                            .ToArray();

            if (scenes.Length == 0)
                throw new InvalidOperationException(
                    "Build Settings 裡沒有啟用任何場景，建置出來會是空的。" +
                    "請把 Assets/Scenes/SampleScene.unity 加進去。");

            Directory.CreateDirectory(OutputFolder);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputFolder,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            CopyFontLicence();
            ReportResult(report);
            return report;
        }

        // ------------------------------------------------------------

        /// <summary>把執行期才用到的著色器加進 Always Included Shaders</summary>
        static void EnsureRuntimeShadersIncluded()
        {
            var graphicsSettings = AssetDatabase
                .LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")
                .FirstOrDefault();

            if (graphicsSettings == null)
            {
                Debug.LogWarning("找不到 GraphicsSettings.asset，無法檢查著色器是否會被剝除。");
                return;
            }

            var serialized = new SerializedObject(graphicsSettings);
            var list = serialized.FindProperty("m_AlwaysIncludedShaders");

            var already = new HashSet<string>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var shader = list.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (shader != null) already.Add(shader.name);
            }

            var added = new List<string>();
            foreach (string name in RuntimeShaders)
            {
                if (already.Contains(name)) continue;

                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogWarning("找不到著色器 " + name + "，略過。");
                    continue;
                }

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                added.Add(name);
            }

            if (added.Count == 0) return;

            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("已把執行期著色器加入 Always Included Shaders：" + string.Join("、", added));
        }

        /// <summary>WebGL 專屬設定。每次建置重套一次，不靠人記得去勾。</summary>
        static void ApplyPlayerSettings()
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.WebGL, ScriptingImplementation.IL2CPP);

            // 瀏覽器沙箱沒有多執行緒，Core 層本來就沒用到
            PlayerSettings.WebGL.threadsSupport = false;

            // Gzip 搭配伺服器的 Content-Encoding 標頭。
            // 開啟 fallback：萬一標頭沒設對，Unity 會自己在前端解壓，
            // 至少不會變成一片空白的頁面。
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;

            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;

            PlayerSettings.runInBackground = true;
            PlayerSettings.companyName = "Mahjong Prototype";
            PlayerSettings.productName = "台灣十六張麻將";

            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.WebGL, ManagedStrippingLevel.Low);
        }

        /// <summary>
        /// 字型是 SIL OFL 授權，隨產品散布時要附上授權全文。
        /// 放進建置輸出，部署上去就跟著在。
        /// </summary>
        static void CopyFontLicence()
        {
            const string source = "docs/licenses/OFL.txt";
            if (!File.Exists(source))
            {
                Debug.LogWarning("找不到 " + source + "，字型授權沒有一起打包。");
                return;
            }

            string target = Path.Combine(OutputFolder, "OFL.txt");
            if (Directory.Exists(OutputFolder)) File.Copy(source, target, overwrite: true);
        }

        static void ReportResult(BuildReport report)
        {
            var summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError(string.Format("WebGL 建置失敗：{0}，錯誤 {1} 項",
                                             summary.result, summary.totalErrors));
                return;
            }

            Debug.Log(string.Format("WebGL 建置完成｜輸出 {0}｜大小 {1:N1} MB｜耗時 {2:N0} 秒",
                                    OutputFolder,
                                    summary.totalSize / 1024f / 1024f,
                                    summary.totalTime.TotalSeconds));
        }
    }
}
